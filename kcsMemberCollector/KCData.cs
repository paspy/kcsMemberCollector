using System.Collections.Generic;

namespace kcsMemberCollector {

    class DeckShip {
        public uint Ship_id { get; set; }
        public uint Ship_level { get; set; }
        public uint Ship_star { get; set; }
    }

    class KCData {
        public uint Member_id { get; set; }
        public string Nickname { get; set; }
        public string Comment { get; set; }
        public uint Rank { get; set; }
        public uint Level { get; set; }
        public uint Exp { get; set; }
        public uint Friend_count { get; set; }
        public uint Ship_current { get; set; }
        public uint Ship_max { get; set; }
        public uint Item_current { get; set; }
        public uint Item_max { get; set; }
        public uint Furniture_count { get; set; }
        public string Deckname { get; set; }
        public List<DeckShip> DeckList { get; set; }
        public long Update { get; set; }
    }
}
