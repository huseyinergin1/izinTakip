using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using yillikizin.Models;

namespace yillikizin.Models
{
    // HareketViewModel.cs
    public class HareketViewModel
    {
        public List<personel> PersonelListesi { get; set; }
        public List<Hareket> HareketListesi { get; set; }
        public int? SelectedPersonelId { get; set; }  // Seçilen personel ID
        public int? SelectedPersonelKartNo { get; set; } // Seçilen personelin kart no'su, int türünde
    }

}
