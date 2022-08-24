using System.Collections.Generic;
using System.Xml.Serialization;

namespace NextcloudClient.Models
{
    [XmlRoot("multistatus")]
    public abstract class BaseResponse<T>
    {
        [XmlElement("response")]
        public List<T> Response { get; set; }
    }
}