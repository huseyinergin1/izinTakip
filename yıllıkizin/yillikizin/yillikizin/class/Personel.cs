using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace yillikizin.Models
{
    public class personel
    {
        public int id { get; set; }
        public string adi { get; set; }
        public string soyadi { get; set; }
        public string sicilno { get; set; }
        public string unvan { get; set; }
        public string resim { get; set; }
        public DateTime? dogumtarih { get; set; }
        public DateTime? isegiristarih { get; set; }

        public int? DepartmanId { get; set; } // Departman ile ilişki için
        public virtual Departman Departman { get; set; } // Departman nesnesi
    }
}
