using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace TubumuMeeting.GB28181.Models
{
    [XmlRoot("Notify")]
    public class Notify
    {
        /// <summary>
        /// 命令类型：设备信息查询(必选)
        /// </summary>
        [XmlElement("CmdType")]
        public CommandType CommandType { get; set; }

        [XmlElement("SN")]
        public int SN { get; set; }

        [XmlElement("DeviceID")]
        public string DeviceID { get; set; }

        [XmlElement("SumNum")]
        public int SumNum { get; set; }

        [XmlElement("DeviceList")]
        public DeviceList DeviceList { get; set; }
    }

    public class DeviceList
    {
        [XmlAttribute("Num")]
        public int Num { get; set; }

        [XmlElement("Item")]
        public List<Item> Items { get; set; }
    }

    public class Item
    {
        [XmlElement("DeviceID")]
        public string DeviceID { get; set; }

        [XmlElement("Event")]
        public string Event { get; set; }
    }
}
