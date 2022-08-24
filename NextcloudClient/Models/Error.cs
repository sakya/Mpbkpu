using System.Xml.Serialization;

namespace NextcloudClient.Models
{
    [XmlRoot("error")]
    public class Error
    {
        [XmlElement("exception", Namespace = "http://sabredav.org/ns")]
        public string Exception { get; set; }

        [XmlElement("message", Namespace = "http://sabredav.org/ns")]
        public string Message { get; set; }
    }
}