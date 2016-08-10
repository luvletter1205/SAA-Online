﻿using System;
using System.Data;
using System.Net.Mail;
using Newtonsoft.Json.Linq;

namespace SAAO
{
    /// <summary>
    /// Mail 邮件
    /// </summary>
    public class Mail
    {
        /// <summary>
        /// Hmailserver database connection string
        /// </summary>
        public static string ConnStr = System.Configuration.ConfigurationManager.ConnectionStrings["SAAMConnectionString"].ToString();
        /// <summary>
        /// SMTP server (localhost most possibly)
        /// </summary>
        public static string ServerAddress = System.Configuration.ConfigurationManager.AppSettings["mailServerAddress"];
        /// <summary>
        /// Mail domain ("xuehuo.org" for example)
        /// </summary>
        public static string MailDomain = System.Configuration.ConfigurationManager.AppSettings["mailDomainName"];
        /// <summary>
        /// Hmailserver local data storage path
        /// </summary>
        private readonly string _mailPath = System.Configuration.ConfigurationManager.AppSettings["mailStoragePath"] + MailDomain + @"\";
        /// <summary>
        /// Mail index in database
        /// </summary>
        private readonly int _mailId;
        /// <summary>
        /// Path to eml file of the mail
        /// </summary>
        private readonly string _emlPath;
        /// <summary>
        /// CDO Message (read from eml file)
        /// </summary>
        private readonly CDO.Message _message;
        /// <summary>
        /// Username of the user the mail belongs to
        /// </summary>
        public string Username;
        public string Subject;
        public MailAddress From;
        public MailAddress[] To;
        public DateTime SentOn;
        public int AttachmentCount;

        /// <summary>
        /// Meaning of IMAP flag (refer to Hmailserver open source project)
        /// </summary>
        public enum MailFlag
        {
            Seen = 1,
            Deleted = 2,
            Flagged = 4,
            Answered = 8,
            Draft = 16,
            Recent = 32,
            VirusScan = 64
        };

        /// <summary>
        /// IMAP flag
        /// </summary>
        public MailFlag Flag;
        /// <summary>
        /// Folder ID in database (distinct for each user)
        /// </summary>
        private int _folderid;
        /// <summary>
        /// Mail address structure
        /// </summary>
        public struct MailAddress
        {
            /// <summary>
            /// Displayed name
            /// </summary>
            public string Name;
            /// <summary>
            /// Email address
            /// </summary>
            public string Mail;
            /// <summary>
            /// mailAddress constructor
            /// </summary>
            /// <param name="name">Displayed name</param>
            /// <param name="mail">Email address</param>
            public MailAddress(string name, string mail)
            {
                // Remove quotation marks
                Name = name.Replace("\"","");
                Mail = mail.Replace("\"","");
            }
        }
        /// <summary>
        /// Mail constructor
        /// </summary>
        /// <param name="mailId">Mail ID in database</param>
        public Mail(int mailId)
        {
            _mailId = mailId;
            SqlIntegrate si = new SqlIntegrate(ConnStr);
            var mailInfo = si.Reader($"SELECT * FROM hm_messages WHERE messageid = {mailId}");
            Username = si.Query($"DECLARE @uid int; SELECT @uid = messageaccountid FROM hm_messages WHERE messageid = {mailId}; SELECT accountaddress FROM hm_accounts WHERE accountid = @uid;").ToString().Replace("@" + MailDomain, "");
            /* The eml file is storage in this way:
             *
             * When hmailserver receives an email, it writes information in database and stores the eml file.
             * 
             * [A] = Hmailserver data path (need to set mailStoragePath in Web.config)
             * 
             * [B] = Domain name (SAAO.Mail.mailDomain, need to set mailDomainName in Web.config)
             * 
             * [C] = Username (without domain name, "szhang140" for example)
             * 
             * [D] = Initial two characters (contain no open brace '{') of eml file name (store in [messagefilename] in database)
             * 
             * [E] = Eml file name
             * 
             * [F] = [A] \ [B] \ [C] \ [D] \ [E] ("D:\Mail_Data\xuehuo.org\szhang140\1B\{1B961C98-4FC8-40E3-BC8E-FA5A1D374381}.eml" for example)
             * 
             * [F] is the absolute path to eml file.
             */
            _emlPath = _mailPath + Username + @"\" + mailInfo["messagefilename"].ToString().Substring(1, 2) + @"\" + mailInfo["messagefilename"];
            _message = ReadEml(_emlPath);
            Flag = (MailFlag)Convert.ToInt32(mailInfo["messageflags"]);
            Subject = _message.Subject;
            // Remove '<' and '>'
            From = new MailAddress(_message.From.Split('<')[0], _message.From.Split('<')[1].Replace(">", ""));
            int toCount = _message.To.Trim().Split(',').Length;
            To = new MailAddress[toCount];
            for (int i = 0; i < toCount; i++)
                To[i] = new MailAddress(_message.To.Trim().Split(',')[i].Split('<')[0], _message.To.Trim().Split(',')[i].Split('<')[1].Replace(">",""));
            SentOn = _message.SentOn;
            AttachmentCount = _message.Attachments.Count;
        }
        /// <summary>
        /// Obtain mail body (if not HTML, convert it to HTML)
        /// </summary>
        /// <returns>Mail body (HTML)</returns>
        public string Body()
        {
            if (_message.HTMLBody != "")
                return _message.HTMLBody;
            // Plain text mail body
            return "<p>" + _message.TextBody.Replace("\n\r", "<br>").Replace("\n", "<br>") + "</p>";
            // Both unix-style and Windows-style returns
        }
        /// <summary>
        /// Obtain mail body preview (default length 80)
        /// </summary>
        /// <returns>Mail body preview</returns>
        public string Thumb(int length = 80)
        {
            string mailbody = _message.HTMLBody != "" ? FilterHtml(_message.HTMLBody) : _message.TextBody;
            if (mailbody.Length > length)
                mailbody = mailbody.Substring(0, length);
            return mailbody.Replace("\"", "").Replace("\n", "").Replace("\r", "");
        }
        /// <summary>
        /// Get attachment name
        /// </summary>
        /// <param name="index">Attachment index (from 1)</param>
        /// <returns>Attachment name</returns>
        private string GetAttachmentName(int index)
        {
            if (index <= _message.Attachments.Count && index != 0)
                return _message.Attachments[index].FileName;
            return null;
        }

        /// <summary>
        /// Get attachment storage path
        /// </summary>
        /// <param name="index">Attachment index (from 1)</param>
        /// <returns>Attachment storage path</returns>
        private string GetAttachmentPath(int index)
        {
            if (index <= _message.Attachments.Count && index != 0)
                return _emlPath.Replace(".eml", "") + "_" + index + ".attach";
            return null;
        }
        /// <summary>
        /// Download mail attachment (via current response)
        /// </summary>
        /// <param name="index">Attachment index (from 1)</param>
        public void DownloadAttachment(int index)
        {
            Utility.Download(GetAttachmentPath(index), GetAttachmentName(index));
        }
        /// <summary>
        /// Obtain attachment information in JSON
        /// </summary>
        /// <returns>Json array of attachment information</returns>
        private JArray AttachmentJson()
        {
            JArray a = new JArray();
            if (AttachmentCount != 0)
            {
                for (int i = 1; i <= AttachmentCount; i++)
                {
                    /* For eml path "[PATH TO EML FILE]\{[GUID]}.eml"
                     * its attachment was save in "[PATH TO EML FILE]\{[GUID]}_[INDEX].attach"
                     * [INDEX] was count from 1
                     */
                    if (!System.IO.File.Exists(_emlPath.Replace(".eml", "") + "_" + i + ".attach"))
                        _message.Attachments[i].SaveToFile(_emlPath.Replace(".eml", "") + "_" + i + ".attach");
                    a.Add(new JObject { ["filename"] = _message.Attachments[i].FileName });
                }
            }
            return a;
        }
        /// <summary>
        /// Move the mail to another folder
        /// </summary>
        /// <param name="folderName">Target folder name</param>
        public void MoveTo(string folderName)
        {
            SqlIntegrate si = new SqlIntegrate(ConnStr);
            int uid = Convert.ToInt32(si.Query($"SELECT accountid FROM hm_accounts WHERE accountaddress = '{User.Current.Mail}'"));
            int folderid = Convert.ToInt32(si.Query($"SELECT folderid FROM hm_imapfolders WHERE foldername = '{folderName}' AND folderaccountid = {uid}"));
            // TODO: Care for SQL Inject of folderName
            si.Execute($"UPDATE hm_messages SET messagefolderid = {folderid} WHERE messageid = {_mailId}");
            _folderid = folderid;
        }
        /// <summary>
        /// Set a new flag of the mail
        /// </summary>
        /// <param name="newflag">New flag</param>
        public void SetFlag(MailFlag newflag)
        {
            SqlIntegrate si = new SqlIntegrate(ConnStr);
            si.Execute($"UPDATE hm_messages SET messageflags = {(int) newflag} WHERE messageid = {_mailId}");
            Flag = newflag;
        }
        /// <summary>
        /// Convert the mail information to JSON
        /// </summary>
        /// <returns>Mail information in JSON. {id,subject,from:{name,mail},to:[{name,mail},...],flag,time,attachcount,attachment:[ATTACHMENT JSON]}</returns>
        public JObject ToJson()
        {
            JObject o = new JObject
            {
                ["id"] = _mailId,
                ["subject"] = Subject,
                ["from"] = (JObject) JToken.FromObject(From),
                ["to"] = (JArray) JToken.FromObject(To),
                ["flag"] = (int) Flag,
                ["time"] = SentOn.ToString("yyyy-MM-dd HH:mm:ss"),
                ["attachcount"] = AttachmentCount,
                ["attachment"] = AttachmentJson()
            };
            return o;
        }
        /// <summary>
        /// Filter all HTML tags
        /// </summary>
        /// <param name="strhtml">HTML string</param>
        /// <returns>string without HTML tags</returns>
        private static string FilterHtml(string strhtml)
        {
            string stroutput = strhtml;
            System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex(@"<[^>]+>|</[^>]+>");
            stroutput = regex.Replace(stroutput, "");
            return stroutput;
        }
        /// <summary>
        /// Read eml file into CDO message
        /// </summary>
        /// <param name="filepath">Path to eml file</param>
        /// <returns>CDO message</returns>
        private static CDO.Message ReadEml(string filepath)
        {
            CDO.Message oMsg = new CDO.Message();
            ADODB.Stream stm = new ADODB.Stream();
            stm.Open(System.Reflection.Missing.Value);
            stm.Type = ADODB.StreamTypeEnum.adTypeBinary;
            stm.LoadFromFile(filepath);
            oMsg.DataSource.OpenObject(stm, "_stream");
            stm.Close();
            return oMsg;
        }

        public static void Send(string from, string receiver, string subject, bool isBodyHtml, string body, System.Net.NetworkCredential credential = null)
        {
            if (credential == null)
                credential = new System.Net.NetworkCredential(User.Current.Mail,
                    User.Current.PasswordRaw);
            MailMessage mail = new MailMessage(from, receiver)
            {
                SubjectEncoding = System.Text.Encoding.UTF8,
                Subject = subject,
                IsBodyHtml = true,
                BodyEncoding = System.Text.Encoding.UTF8,
                Body = Utility.Base64Decode(body)
            };
            SmtpClient smtp = new SmtpClient(ServerAddress) {Credentials = credential};
            smtp.Send(mail);
        }
        /// <summary>
        /// List mail(s) of a folder in the database in JSON
        /// </summary>
        /// <param name="folder">Folder name</param>
        /// <returns>JSON of mail(s) of the folder [{id,subject,from,thumb,flag,time,attachcount},...]</returns>
        public static JArray ListJson(string folder)
        {
            SqlIntegrate si = new SqlIntegrate(ConnStr);
            int uid = Convert.ToInt32(si.Query(
                $"SELECT accountid FROM hm_accounts WHERE accountaddress = '{User.Current.Username}@xuehuo.org'"));
            int folderid = Convert.ToInt32(si.Query(
                $"SELECT folderid FROM hm_imapfolders WHERE {(folder == "Sent" ? "(foldername = 'Sent' OR foldername = 'Sent Items' OR foldername = 'Sent Messages')" : "foldername = '" + folder + "'")} AND folderaccountid = {uid}"));
            DataTable list = si.Adapter(
                $"SELECT messageid FROM hm_messages WHERE messagefolderid = {folderid} AND messageaccountid = {uid} ORDER BY messageid DESC");
            JArray a = new JArray();
            for (int i = 0; i < list.Rows.Count; i++)
            {
                Mail message = new Mail(Convert.ToInt32(list.Rows[i]["messageid"]));
                JObject o = new JObject
                {
                    ["id"] = list.Rows[i]["messageid"].ToString(),
                    ["subject"] = message.Subject,
                    ["from"] = message.From.Name.Replace("\"", ""),
                    ["thumb"] = message.Thumb(),
                    ["flag"] = (int) message.Flag,
                    ["time"] = message.SentOn.ToString("yyyy-MM-dd HH:mm"),
                    ["attachcount"] = message.AttachmentCount
                };
                a.Add(o);
            }
            return a;
        }
    }
}