﻿using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Data;
using System.Threading.Tasks;

namespace SAAO
{
    /// <summary>
    /// File 文件
    /// </summary>
    public class File
    {
        /// <summary>
        /// File storage path (with a backslash '\' in the end)
        /// </summary>
        public static string StoragePath = System.Configuration.ConfigurationManager.AppSettings["fileStoragePath"] + @"storage\";
        private readonly string _guid;
        private string _name;
        /// <summary>
        /// File description
        /// </summary>
        private string _info;
        /// <summary>
        /// File extension (doc, pdf, etc.)
        /// </summary>
        private readonly string _extension;
        /// <summary>
        /// File size in byte
        /// </summary>
        private readonly int _size;
        private readonly User _uploader;
        private DateTime _uploadTime;
        private int _downloadCount;
        private readonly string _savePath;
        public List<string> Tag;
        private PermissionLevel _permission;

        public User Uploader { get { return _uploader; } }
        public DateTime UploadTime { get { return _uploadTime; } }
        public string Extension { get { return _extension; } }

        public enum PermissionLevel
        {
            All = 0,
            /// <summary>
            /// Only visible to group of oneself
            /// </summary>
            SelfGroupOnly = 1,
            /// <summary>
            /// Only visible to Senior Two
            /// </summary>
            SeniorTwoOnly = 2,
            /// <summary>
            /// Only visible to important members (administrative member). IMPT_MEMB is defined in Organization.cs
            /// </summary>
            ImptMembOnly = 3
        }
        /// <summary>
        /// File constructor
        /// </summary>
        /// <param name="str">GUID string</param>
        public File(string str)
        {
            Guid guid;
            if (!Guid.TryParse(str, out guid))
                throw new ArgumentException();
            _guid = str.ToUpper();
            var si = new SqlIntegrate(Utility.ConnStr);
            si.AddParameter("@GUID", SqlIntegrate.DataType.VarChar, str.ToUpper());
            var fileInfo = si.Reader("SELECT * FROM [File] WHERE [GUID] = @GUID");
            _name = fileInfo["name"].ToString();
            _info = fileInfo["info"].ToString();
            _extension = fileInfo["extension"].ToString();
            _size = Convert.ToInt32(fileInfo["size"]);
            _uploader = new User(Guid.Parse(fileInfo["uploader"].ToString()));
            _downloadCount = Convert.ToInt32(fileInfo["downloadCount"]);
            _uploadTime = Convert.ToDateTime(fileInfo["uploadTime"]);
            _savePath = StoragePath + str.ToUpper();
            _permission = (PermissionLevel)Convert.ToInt32(fileInfo["permission"]);
            _mediaId = fileInfo["media_id"].ToString();
            Tag = new List<string>();
            si.ResetParameter();
            si.AddParameter("@FUID", SqlIntegrate.DataType.VarChar, str.ToUpper());
            var tagList = si.Adapter("SELECT [name] FROM [Filetag] WHERE FUID = @FUID");
            for (var i = 0; i < tagList.Rows.Count; i++)
                Tag.Add(tagList.Rows[i]["name"].ToString());
        }

        public static string Upload(System.Web.HttpPostedFile file, bool getmediaid = false)
        {
            var guid = Guid.NewGuid().ToString().ToUpper();
            file.SaveAs(StoragePath + guid);
            var si = new SqlIntegrate(Utility.ConnStr);
            si.AddParameter("@GUID", SqlIntegrate.DataType.VarChar, guid);
            si.AddParameter("@name", SqlIntegrate.DataType.VarChar,
                Path.GetFileNameWithoutExtension(file.FileName), 50);
            si.AddParameter("@extension", SqlIntegrate.DataType.VarChar,
                Path.GetExtension(file.FileName).TrimStart('.').ToLower(), 10);
            si.AddParameter("@size", SqlIntegrate.DataType.Int, file.ContentLength);
            si.AddParameter("@UUID", SqlIntegrate.DataType.VarChar, User.Current.UUID);
            si.Execute("INSERT INTO [File] ([GUID],[name],[extension],[size],[uploader]) VALUES (@GUID,@name,@extension,@size,@UUID)");
            if (getmediaid)
            {
                var access_token = SAAO.Utility.GetAccessToken();
                new Task(() =>
                {
                    var jo = (JObject)new JsonSerializer()
                        .Deserialize(new JsonTextReader(new StringReader(Utility.HttpRequest(
                            url:$"https://qyapi.weixin.qq.com/cgi-bin/media/upload?access_token={access_token}&type=file",
                            data: null,
                            filePath: StoragePath + guid,
                            fileName: file.FileName,
                            fileFieldName: "media"))));
                    var _mediaId = jo["media_id"].ToString();
                    var si2 = new SqlIntegrate(Utility.ConnStr);
                    si2.AddParameter("@media_id", SqlIntegrate.DataType.VarChar, _mediaId);
                    si2.AddParameter("@GUID", SqlIntegrate.DataType.VarChar, guid);
                    si2.Execute("UPDATE [File] SET [media_id] = @media_id WHERE [GUID] = @GUID");
                }).Start();
            }
            return guid;
        }
        /// <summary>
        /// Check whether the file has a tag
        /// </summary>
        /// <param name="str">Tag string</param>
        /// <returns>whether the file has this tag</returns>
        public bool HasTag(string str)
        {
            var si = new SqlIntegrate(Utility.ConnStr);
            si.AddParameter("@name", SqlIntegrate.DataType.VarChar, str, 50);
            si.AddParameter("@FUID", SqlIntegrate.DataType.VarChar, _guid);
            var count = Convert.ToInt32(si.Query(
                "SELECT COUNT(*) FROM [Filetag] WHERE [name] = @name AND [FUID] = @FUID"));
            return count != 0;
        }
        /// <summary>
        /// Remove a tag of the file (if existed)
        /// </summary>
        /// <param name="str">Tag string</param>
        public void RemoveTag(string str)
        {
            if (!HasTag(str)) return;
            Tag.Remove(str);
            var si = new SqlIntegrate(Utility.ConnStr);
            si.AddParameter("@name", SqlIntegrate.DataType.NVarChar, str, 50);
            si.AddParameter("@FUID", SqlIntegrate.DataType.VarChar, _guid);
            si.Execute("DELETE FROM [Filetag] WHERE [name] = @name AND [FUID] = @FUID");
        }
        /// <summary>
        /// Add a tag to the file
        /// </summary>
        /// <param name="str">Tag string</param>
        public void AddTag(string str)
        {
            Tag.Add(str);
            var si = new SqlIntegrate(Utility.ConnStr);
            si.AddParameter("@name", SqlIntegrate.DataType.NVarChar, str, 50);
            si.AddParameter("@FUID", SqlIntegrate.DataType.VarChar, _guid);
            si.Execute("INSERT INTO Filetag ([name], [FUID]) VALUES (@name, @FUID)");
        }
        /// <summary>
        /// Download the file (Write stream to current http response)
        /// </summary>
        public void Download()
        {
            _downloadCount++;
            var si = new SqlIntegrate(Utility.ConnStr);
            si.AddParameter("@GUID", SqlIntegrate.DataType.VarChar, _guid);
            si.Execute("UPDATE [File] SET [downloadCount] = [downloadCount] + 1 WHERE [GUID] = @GUID");
            Utility.Download(_savePath, _name + "." + _extension);
        }
        /// <summary>
        /// Filename
        /// </summary>
        public string Name
        {
            set
            {
                _name = value;
                var si = new SqlIntegrate(Utility.ConnStr);
                si.AddParameter("@name", SqlIntegrate.DataType.NVarChar, value, 50);
                si.AddParameter("@GUID", SqlIntegrate.DataType.VarChar, _guid);
                si.Execute("UPDATE [File] SET [name] = @name WHERE [GUID] = @GUID");
            }
            get
            {
                return _name;
            }
        }
        /// <summary>
        /// File description
        /// </summary>
        public string Info
        {
            set
            {
                _info = value;
                var si = new SqlIntegrate(Utility.ConnStr);
                si.AddParameter("@info", SqlIntegrate.DataType.Text, value);
                si.AddParameter("@GUID", SqlIntegrate.DataType.VarChar, _guid);
                si.Execute("UPDATE [File] SET [info] = @info WHERE [GUID] = @GUID");
            }
            get
            {
                return _info;
            }
        }

        private string _mediaId;
        public string MediaId
        {
            get
            {
                if (_size > 1 << 21) // 2 * 1024 * 1024 Byte
                    return null;
                if (_mediaId != "") return _mediaId;
                //SyncGet will lose lots of time
                //MediaId shoule be requested when it's uploaded
                var jo = (JObject)new JsonSerializer()
                    .Deserialize(new JsonTextReader(new StringReader(Utility.HttpRequest(
                        url:
                            $"https://qyapi.weixin.qq.com/cgi-bin/media/upload?access_token={SAAO.Utility.GetAccessToken()}&type=file",
                        data: null,
                        filePath: _savePath,
                        fileName: _name + "." + _extension,
                        fileFieldName: "media"))));
                _mediaId = jo["media_id"].ToString();
                var si = new SqlIntegrate(Utility.ConnStr);
                si.AddParameter("@media_id", SqlIntegrate.DataType.VarChar, _mediaId);
                si.AddParameter("@GUID", SqlIntegrate.DataType.VarChar, _guid);
                si.Execute("UPDATE [File] SET [media_id] = @media_id WHERE [GUID] = @GUID");
                return _mediaId;
            }
        }
        /// <summary>
        /// Delete the file
        /// </summary>
        public void Delete()
        {
            var si = new SqlIntegrate(Utility.ConnStr);
            si.AddParameter("@GUID", SqlIntegrate.DataType.VarChar, _guid);
            si.Execute("DELETE FROM [File] WHERE [GUID] = @GUID");
            si.ResetParameter();
            si.AddParameter("@FUID", SqlIntegrate.DataType.VarChar, _guid);
            si.Execute("DELETE FROM [Filetag] WHERE [FUID] = @FUID");
            System.IO.File.Delete(_savePath);
        }
        /// <summary>
        /// File visibility-level
        /// </summary>
        public PermissionLevel Permission
        {
            get
            {
                return _permission;
            }
            set
            {
                _permission = value;
                var si = new SqlIntegrate(Utility.ConnStr);
                si.AddParameter("@permission", SqlIntegrate.DataType.Int, (int)value);
                si.AddParameter("@GUID", SqlIntegrate.DataType.VarChar, _guid);
                si.Execute("UPDATE [File] SET [permission] = @permission WHERE [GUID] = @GUID");
            }
        }
        /// <summary>
        /// Check whether a user has the permission to the file
        /// </summary>
        /// <param name="user">User</param>
        /// <returns>whether a user has the permission to the file</returns>
        public bool Visible(User user)
        {
            return Visible(_permission, _uploader.UUID, _uploader.Group, user);
        }
        /// <summary>
        /// Check whether a user has the permission to a file (static function)
        /// </summary>
        /// <param name="permission">Permission setting</param>
        /// <param name="uuid">UUID (of uploader)</param>
        /// <param name="group">Group (of uploader)</param>
        /// <param name="user">User (current one most possibly)</param>
        /// <returns>whether a user has the permission to a file</returns>
        /// 
        public static bool Visible(PermissionLevel permission, string uuid, int group, User user)
        {
            return Visible(permission, uuid, group, user.UUID, user.Group, user.Senior, user.IsExecutive);
        }
        /// <summary>
        /// Check whether a user has the permission to a file (bases function)
        /// </summary>
        /// <param name="permission">Permission setting</param>
        /// <param name="uuid">UUID (of uploader)</param>
        /// <param name="group">Group (of uploader)</param>
        /// <param name="user_uuid">User's UUID</param>
        /// <param name="user_group">User's Group</param>
        /// <param name="user_senior">User's Senior</param>
        /// <param name="user_isExecutive">User's isExecutive</param>
        /// <returns>whether a user has the permission to a file</returns>
        public static bool Visible(PermissionLevel permission, string uuid, int group, string user_uuid, int user_group, int user_senior, bool user_isExecutive)
        {
            if (uuid == user_uuid)
                return true;
            switch (permission)
            {
                case PermissionLevel.All:
                    return true;
                case PermissionLevel.SelfGroupOnly:
                    if (group == user_group)
                        return true;
                    break;
                case PermissionLevel.SeniorTwoOnly:
                    if (user_senior == 2)
                        return true;
                    break;
                case PermissionLevel.ImptMembOnly:
                    if (user_isExecutive)
                        return true;
                    break;
                default:
                    return false;
            }
            return false;
        }
        /// <summary>
        /// Convert the file information to JSON
        /// </summary>
        /// <returns>File information in JSON. {guid,permission,name,extension,uploadTime,size,uploader,group,downloadCount,tag(string),info(string)}</returns>
        public JObject ToJson()
        {
            var o = new JObject
            {
                ["guid"] = _guid,
                ["permission"] = (int)_permission,
                ["name"] = _name,
                ["extension"] = _extension,
                ["uploadTime"] = _uploadTime.ToString("yyyy-MM-dd HH:mm"),
                ["size"] = _size,
                ["uploader"] = _uploader.Realname,
                ["group"] = _uploader.GroupName,
                ["downloadCount"] = _downloadCount,
                ["tag"] = string.Join(",", Tag),
                ["info"] = _info ?? "",
                ["wechat"] = _size <= 1 << 21 && DateTime.Now < _uploadTime.AddDays(3) && User.Current.Wechat != ""
            };
            return o;
        }
        /// <summary>
        /// List current files in the database in JSON
        /// </summary>
        /// <returns>JSON of current files [{guid,name,extension,uploaderName,datetime,info(bool)},...]</returns>
        public static JArray ListJson(DateTime start, DateTime end)
        {
            var si = new SqlIntegrate(Utility.ConnStr);
            si.AddParameter("@start", SqlIntegrate.DataType.Date, start);
            si.AddParameter("@end", SqlIntegrate.DataType.Date, end);
            var dt = si.Adapter("SELECT [File].*, [User].[realname], [User].[group] FROM [File] INNER JOIN [User] ON [File].[uploader] = [User].[UUID] AND [File].[uploadTime] BETWEEN @start AND @end ORDER BY [File].[ID] DESC");
            var a = new JArray();
            for (var i = 0; i < dt.Rows.Count; i++)
            {
                if (
                    !Visible((PermissionLevel)Convert.ToInt32(dt.Rows[i]["permission"].ToString()),
                        dt.Rows[i]["uploader"].ToString(), Convert.ToInt32(dt.Rows[i]["group"]),
                        User.Current)) continue;
                var o = new JObject
                {
                    ["guid"] = dt.Rows[i]["GUID"].ToString(),
                    ["name"] = dt.Rows[i]["name"].ToString(),
                    ["extension"] = dt.Rows[i]["extension"].ToString(),
                    ["downloadCount"] = int.Parse(dt.Rows[i]["downloadCount"].ToString()),
                    ["uploaderName"] = dt.Rows[i]["realname"].ToString(),
                    ["datetime"] = DateTime.Parse(dt.Rows[i]["uploadTime"].ToString()).ToString("yyyy-MM-dd HH:mm"),
                    ["info"] = dt.Rows[i]["info"].ToString() != ""
                };
                a.Add(o);
            }
            return a;
        }
        /// <summary>
        /// Get All Users who will receive File Upload Event Push
        /// </summary>
        /// <returns>List of WechatID For sending message by WechatAPI</returns>
        public List<string> GetVisibleUserWechat()
        {
            List<string> wechats = new List<string>();
            var si = new SqlIntegrate(Utility.ConnStr);
            DataTable dt = si.Adapter("SELECT [UUID],[wechat],[group],[sn],[job] FROM [User] WHERE [activated]=1 AND FilePush=1 AND [wechat]<>''"); //Get All Used Wechat File Upload Event Push Service
            foreach (DataRow dr in dt.Rows)
            {
                string _sn = dr["sn"].ToString();
                int job = Convert.ToInt32(dr["job"].ToString());
                int senior = 1;
                if (_sn.Substring(0, 4) == Organization.Current.State.SeniorOne)
                    senior = 1;
                else if (Convert.ToInt32(_sn.Substring(0, 4)) <= Convert.ToInt32(Organization.Current.State.SeniorTwo))
                    senior = 2;
                bool isexecutive = senior == 2 && Organization.Current.Structure.Select("[executive] = 1 AND [job] = " + job).Length != 0;
                string user_uuid = dr["UUID"].ToString();
                int user_group = Convert.ToInt32(dr["group"]);
                if (Visible(Permission, Uploader.UUID, Uploader.Group, user_uuid, user_group, senior, isexecutive))
                    wechats.Add(dr[1].ToString());
            }
            return wechats;
        }
        
        public struct DownloadToken
        {
            public string File_GUID;
            public long TimeStamp;
        }
        /// <summary>
        /// TempDownloadToken. To Download File In Wechat.
        /// </summary>
        public static Dictionary<string, DownloadToken> TempDownloadToken
        {
            get
            {
                if (System.Web.HttpContext.Current.Application["TempDownloadToken"] == null)
                    System.Web.HttpContext.Current.Application["TempDownloadToken"] = new Dictionary<string, DownloadToken>();
                return (Dictionary<string, DownloadToken>)System.Web.HttpContext.Current.Application["TempDownloadToken"];
            }
        }

    }
}