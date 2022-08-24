using System;
using System.Globalization;
using System.Xml.Serialization;

namespace NextcloudClient.Models
{
    [XmlRoot("propstat")]
    public class PropStat
    {
        public class Prop
        {
            public class ResourceTypes
            {
                [XmlElement("collection")]
                public string Collection { get; set; }
                [XmlElement("version-history")]
                public string VersionHistory { get; set; }
                [XmlElement("activity")]
                public string Activity { get; set; }
                [XmlElement("baseline")]
                public string Baseline { get; set; }
            }

            [XmlElement("resourcetype")]
            public ResourceTypes ResourceType { get; set; }

            [XmlElement("getlastmodified")]
            public string LastModified { get; set; }

            [XmlIgnore]
            public DateTime? LastModifiedDate
            {
                get
                {
                    if (string.IsNullOrEmpty(LastModified))
                        return null;

                    if (DateTime.TryParseExact(LastModified, "r", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var res))
                        return res;
                    return null;
                }
            }

            [XmlElement("getetag")]
            public string Etag { get; set; }

            [XmlElement("getcontenttype")]
            public string ContentType { get; set; }

            [XmlElement("fileid", Namespace = "http://owncloud.org/ns")]
            public string FileId { get; set; }

            [XmlElement("permissions", Namespace = "http://owncloud.org/ns")]
            public string Permissions { get; set; }

            [XmlElement("size", Namespace = "http://owncloud.org/ns")]
            public long Size { get; set; }

            [XmlElement("getcontentlength")]
            public long ContentLength { get; set; }

            [XmlElement("has-preview", Namespace = "http://owncloud.org/ns")]
            public bool HasPreview { get; set; }

            [XmlElement("favorite", Namespace = "http://owncloud.org/ns")]
            public int Favorite { get; set; }

            [XmlElement("comments-href", Namespace = "http://owncloud.org/ns")]
            public string CommentsHref { get; set; }

            [XmlElement("comments-count", Namespace = "http://owncloud.org/ns")]
            public int CommentsCount { get; set; }

            [XmlElement("comments-unread", Namespace = "http://owncloud.org/ns")]
            public int CommentsUnread { get; set; }

            [XmlElement("owner-id", Namespace = "http://owncloud.org/ns")]
            public string OwnerId { get; set; }

            [XmlElement("owner-display-name", Namespace = "http://owncloud.org/ns")]
            public string OwnerDisplayName { get; set; }

            [XmlElement("contained-folder-count", Namespace = "http://nextcloud.org/ns")]
            public int? ContainedFolderCount { get; set; }

            [XmlElement("contained-file-count", Namespace = "http://nextcloud.org/ns")]
            public int? ContainedFileCount { get; set; }
        }

        [XmlElement("status")]
        public string Status { get; set; }

        [XmlElement("prop")]
        public Prop Properties { get; set; }
    }
}