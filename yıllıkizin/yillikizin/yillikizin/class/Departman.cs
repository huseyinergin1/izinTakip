using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace yillikizin.Models
{
    public class Departman
    {
        public int Id { get; set; }
        public string departmanName { get; set; }

        // Personel ile ilişki
        public virtual ICollection<personel> personeller { get; set; }
    }
}
