using System.Collections.Generic;
using System.Web.Mvc;
using System;
using yillikizin.Models;
using System.Linq;
using System.IO;
using System.Globalization;

public class HareketController : Controller
{
    // Personel listesi sayfası
    public ActionResult Index()
    {
        var model = new HareketViewModel
        {
            PersonelListesi = GetPersonelListesi(),
            HareketListesi = new List<Hareket>(), // Başlangıçta boş hareket listesi
            SelectedPersonelId = null
        };

        return View(model);
    }

    // Seçilen personelin hareketlerini almak için
    public ActionResult GetHareketler(int personelId)
    {
        var hareketListesi = GetHareketlerByPersonelId(personelId);
        return PartialView("_HareketListesi", hareketListesi); // Kısmi görünüm döndürüyoruz
    }

    // Belirli bir tarih aralığına göre hareketleri filtrelemek için
    public ActionResult FilterHareketler(int personelId, DateTime baslangicTarihi, DateTime bitisTarihi)
    {
        var hareketListesi = GetHareketlerByPersonelId(personelId);

        // Tarih aralığına göre filtreleme
        hareketListesi = hareketListesi.Where(h => h.Tarih >= baslangicTarihi && h.Tarih <= bitisTarihi).ToList();

        return PartialView("_HareketListesi", hareketListesi); // Filtrelenmiş hareket listesi döndürüyoruz
    }

    private List<Hareket> GetHareketlerByPersonelId(int personelId)
    {
        // Personelin kart numarasını al
        var personel = GetPersonelById(personelId);
        if (personel == null)
        {
            return new List<Hareket>(); // Personel bulunamazsa boş liste döndür
        }

        // Kart numarasına göre hareketleri filtrele
        var hareketListesi = ReadHareketFromFile()
            .Where(h => h.kartno == personel.kartno)
            .OrderBy(h => h.Tarih)
            .ThenBy(h => h.Saat)
            .ToList();

        // Her hareketin işlem tipini ayarlama
        foreach (var hareket in hareketListesi)
        {
            // Dosyadan okunan işlem tipine göre ayarlıyoruz
            if (hareket.IslemTipi == "Giriş") // 001 durumu
            {
                hareket.IslemTipi = "Giriş";
            }
            else if (hareket.IslemTipi == "Çıkış") // 002 durumu
            {
                hareket.IslemTipi = "Çıkış";
            }
        }

        return hareketListesi; // Son listeyi döndür
    }

    private personel GetPersonelById(int personelId)
    {
        using (var db = new YillikizinEntities())
        {
            return db.personel.FirstOrDefault(p => p.id == personelId);
        }
    }

    private List<personel> GetPersonelListesi()
    {
        using (var db = new YillikizinEntities())
        {
            return db.personel.ToList();
        }
    }

    private List<Hareket> ReadHareketFromFile()
    {
        var hareketler = new List<Hareket>();
        string filePath = @"C:\WebPDKS2\Transferonline Merkez 2.0.0.5\Veriler\a.txt"; // Dosya yolu

        if (!System.IO.File.Exists(filePath))
        {
            Console.WriteLine("Hareket verilerini içeren dosya bulunamadı.");
            return hareketler; // Boş liste döndür
        }

        string[] lines = System.IO.File.ReadAllLines(filePath);

        foreach (var line in lines)
        {
            var parts = line.Split(',');

            if (parts.Length >= 5)
            {
                int kartno;
                DateTime tarih;
                TimeSpan saat;

                if (int.TryParse(parts[1], out kartno) &&
                    DateTime.TryParse(parts[3], out tarih) &&
                    TimeSpan.TryParse(parts[4], out saat))
                {
                    // İşlem tipini belirle
                    string islemTipi = parts[0] == "001" ? "Giriş" : "Çıkış";

                    var hareket = new Hareket
                    {
                        kartno = kartno,
                        TerminalNo = parts[2],
                        Tarih = tarih,
                        Saat = saat,
                        IslemTipi = islemTipi // İşlem tipini burada belirliyoruz
                    };
                    hareketler.Add(hareket);
                }
                else
                {
                    Console.WriteLine($"Geçersiz veri: {line}");
                }
            }
        }

        return hareketler;
    }

    [HttpPost]
    public ActionResult YeniHareketEkle(int kartNo, string terminalNo, string tarih, string saat, string yon)
    {
        try
        {
            // Validate kartNo and terminalNo
            if (kartNo <= 0 || string.IsNullOrWhiteSpace(terminalNo))
            {
                return Json(new { success = false, message = "Kart No ve Terminal No geçerli olmalıdır." });
            }

            // Validate tarih and saat format
            if (!DateTime.TryParseExact(tarih + " " + saat, "yyyy-MM-dd HH:mm", null, DateTimeStyles.None, out DateTime hareketTarihi))
            {
                return Json(new { success = false, message = "Tarih ve saat formatı hatalı. Lütfen 'yyyy-MM-dd HH:mm' formatında girin." });
            }

            // Prepare yeniHareket string
            string yeniHareket = $"{yon},{kartNo:D10},{terminalNo},{hareketTarihi:yyyy/MM/dd},{hareketTarihi:HH:mm:ss}";

            string filePath = @"C:\WebPDKS2\Transferonline Merkez 2.0.0.5\Veriler\a.txt";

            // Write to file
            using (StreamWriter sw = new StreamWriter(filePath, true))
            {
                sw.WriteLine(yeniHareket);
            }

            return Json(new { success = true, message = "Hareket başarıyla eklendi." });
        }
        catch (Exception ex)
        {
            // Log the exception if necessary (optional)
            return Json(new { success = false, message = "Harekete eklenirken bir hata oluştu.", error = ex.Message });
        }
    }
    [HttpPost]
    public ActionResult UpdateHareket(int kartNo, string tarih, string saat, string yon)
    {
        try
        {
            string filePath = @"C:\WebPDKS\Transferonline Merkez 2.0.0.5\Veriler\a.txt";
            var lines = System.IO.File.ReadAllLines(filePath).ToList();
            DateTime hareketTarihi = DateTime.ParseExact(tarih + " " + saat, "yyyy-MM-dd HH:mm", null);

            bool isUpdated = false;

            for (int i = 0; i < lines.Count; i++)
            {
                var parts = lines[i].Split(',');

                if (parts.Length >= 5)
                {
                    int mevcutKartNo;
                    DateTime mevcutTarih;
                    TimeSpan mevcutSaat;

                    if (int.TryParse(parts[1], out mevcutKartNo) &&
                        DateTime.TryParse(parts[3], out mevcutTarih) &&
                        TimeSpan.TryParse(parts[4], out mevcutSaat))
                    {
                        // Kart No ve Tarih'e göre satırı buluyoruz
                        if (mevcutKartNo == kartNo && mevcutTarih == hareketTarihi.Date)
                        {
                            // Sadece değiştirilen alanları güncelliyoruz.
                            if (saat != null && saat != "")
                            {
                                parts[4] = saat;  // Saat'i güncelle
                            }
                            if (yon != null && yon != "")
                            {
                                parts[0] = yon == "Giriş" ? "001" : "002";  // Yönü güncelle
                            }
                            if (tarih != null && tarih != "")
                            {
                                parts[3] = tarih; // Tarihi güncelle
                            }

                            lines[i] = string.Join(",", parts); // Güncellenmiş satır
                            isUpdated = true;
                        }
                    }
                }
            }

            if (isUpdated)
            {
                // Dosyayı yeniden yaz
                System.IO.File.WriteAllLines(filePath, lines);
                return Json(new { success = true, message = "Hareket başarıyla güncellendi." });
            }
            else
            {
                return Json(new { success = false, message = "Veri bulunamadı veya değişiklik yapılmadı." });
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Güncelleme sırasında bir hata oluştu.", error = ex.Message });
        }
    }

}
