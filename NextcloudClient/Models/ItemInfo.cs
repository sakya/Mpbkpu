using System.Xml.Serialization;

namespace NextcloudClient.Models
{
    [XmlRoot("multistatus")]
    public class ItemInfo : BaseResponse<Item>
    {
    }

    [XmlRoot("response")]
    public class Item
    {
        [XmlElement("href")]
        public string Href { get; set; }

        [XmlElement("propstat")]
        public PropStat PropStat { get; set; }
    }
}