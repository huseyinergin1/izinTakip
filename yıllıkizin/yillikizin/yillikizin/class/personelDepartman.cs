using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace yillikizin
{
    public class Departman
    {
        public int Id { get; set; }
        public string DepartmanName { get; set; }
        public virtual ICollection<Personel> Personeller { get; set; } // Bir departmanın birden fazla personeli olabilir
    }

    public class Personel
    {
        public int Id { get; set; }
        public string Adi { get; set; }
        public string Soyadi { get; set; }
        public int DepartmanID { get; set; } // Yabancı anahtar
        public virtual Departman Departman { get; set; } // İlişki
    }
}
