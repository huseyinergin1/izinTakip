using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System;

namespace yillikizin.Models
{
    public class Cihaz
    {
        public int Id { get; set; } // Id alanı
        public string IpAdresi { get; set; } // IP adresi
        public string Model { get; set; } // Cihaz modeli
        public int Port { get; set; } // Port
        public bool IsConnected { get; set; } // Bağlantı durumu
    }
}
