using Quartz;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.Collections.Generic;
using yillikizin.Models;
using yillikizin.Controllers;
using Rotativa;
using System.Web.Mvc;

public class EmailJob : IJob
{
    private YillikizinEntities db = new YillikizinEntities();

    public async Task Execute(IJobExecutionContext context)
    {
        Console.WriteLine("EmailJob tetiklendi.");
        try
        {
            var settingsList = db.EmailSettings.ToList();
            var emailService = new EmailService();
            string subject = "Günlük Rapor";
            string body = "Bu, günlük hareket değerlendirme raporudur.";

            // Raporu oluştur
            var reportModel = HareketDegerlendirmeReport(DateTime.Now.AddDays(-1), DateTime.Now);
            var pdfBytes = GeneratePdfStream(reportModel);

            using (var pdfStream = new MemoryStream(pdfBytes))
            {
                foreach (var settings in settingsList)
                {
                    pdfStream.Position = 0; // Stream'i başa döndür
                    emailService.SendEmailWithAttachmentAsync(settings.RecipientEmails, subject, body, pdfStream, "HareketDegerlendirmeRaporu.pdf");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"E-posta gönderimi başarısız: {ex.Message}");
        }
    }
    private byte[] GeneratePdfStream(HareketViewModel model)
    {
        using (var stream = new MemoryStream())
        {
            var vardiyaListesi = GetVardiyaListesi();
            var pdfDoc = new Document(PageSize.A4, 25, 25, 15, 15); // PDF sayfa boyutu ve boşlukları
            PdfWriter.GetInstance(pdfDoc, stream);
            pdfDoc.Open();

            // Arayüzde kullanılan fontu tanımla
            string fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
            BaseFont baseFont = BaseFont.CreateFont(fontPath, BaseFont.IDENTITY_H, BaseFont.NOT_EMBEDDED);
            Font font = new Font(baseFont, 8); // Font boyutunu küçülttük
            Font headerFont = new Font(baseFont, 8, Font.BOLD); // Başlık fontu

            // Tarih ve tablo arasında boşluk ekleyelim
            pdfDoc.Add(new Phrase("\n"));

            // Yeni bir PdfPTable oluşturuyoruz ve başlıkları belirliyoruz
            var table = new PdfPTable(8); // 8 sütun: Tarih, Ad, Soyad, Giriş Saatleri, Çıkış Saatleri, Çalışma Süresi, Fazla Mesai, Eksik Çalışma
            table.WidthPercentage = 100; // Tablo genişliği
            table.SetWidths(new float[] { 3f, 2.5f, 2.5f, 2f, 2f, 2f, 2f, 2f }); // Sütun genişliklerini ayarladık

            // Başlıklar
            AddStyledCell(table, "Tarih", headerFont, isHeader: true);
            AddStyledCell(table, "Ad", headerFont, isHeader: true);
            AddStyledCell(table, "Soyad", headerFont, isHeader: true);
            AddStyledCell(table, "Giriş Saatleri", headerFont, isHeader: true);
            AddStyledCell(table, "Çıkış Saatleri", headerFont, isHeader: true);
            AddStyledCell(table, "Çalışma Süresi", headerFont, isHeader: true);
            AddStyledCell(table, "Eksik Çalışma", headerFont, isHeader: true);
            AddStyledCell(table, "Fazla Mesai", headerFont, isHeader: true);

            // Veriler
            bool isAlternateRow = false;
            foreach (var hareket in model.HareketListesi)
            {
                isAlternateRow = !isAlternateRow;
                var backgroundColor = isAlternateRow ? new BaseColor(230, 230, 230) : BaseColor.WHITE;

                // Tarih bilgisi
                AddStyledCell(table, hareket.Tarih.ToString("dd-MM-yyyy ddd"), font, backgroundColor);

                // Personel adı ve soyadı
                AddStyledCell(table, hareket.personelAdi ?? "Bilinmiyor", font, backgroundColor);
                AddStyledCell(table, hareket.personelSoyadi ?? "Bilinmiyor", font, backgroundColor);

                // Giriş saatleri
                string girisSaatleri = hareket.GirişSaatleri.Any()
                    ? string.Join(", ", hareket.GirişSaatleri.Select(g => g.ToString(@"hh\:mm")))
                    : "Giriş Yok";
                AddStyledCell(table, girisSaatleri, font, backgroundColor);

                // Çıkış saatleri
                string cikisSaatleri = hareket.ÇıkışSaatleri.Any()
                    ? string.Join(", ", hareket.ÇıkışSaatleri.Select(c => c.ToString(@"hh\:mm")))
                    : "Çıkış Yok";
                AddStyledCell(table, cikisSaatleri, font, backgroundColor);

                // Çalışma süresi, eksik çalışma süresi ve fazla mesai hesaplamaları
                string calismaSuresi = "Veri Eksik";
                string eksikSureText = "10:00";
                string fazlaMesaiText = "00:00";

                if (!hareket.GirişSaatleri.Any() && !hareket.ÇıkışSaatleri.Any())
                {
                    // Hem giriş hem çıkış yoksa "Devamsız" yaz
                    calismaSuresi = "Devamsız";
                }
                else if (hareket.GirişSaatleri.Any() && hareket.ÇıkışSaatleri.Any())
                {
                    // Hem giriş hem çıkış varsa çalışma süresini hesapla
                    var girisSaat = hareket.GirişSaatleri.First();
                    var cikisSaat = hareket.ÇıkışSaatleri.First();

                    if (girisSaat != null && cikisSaat != null)
                    {
                        var calismaSuresiTimeSpan = cikisSaat - girisSaat;
                        calismaSuresi = calismaSuresiTimeSpan.ToString(@"hh\:mm");

                        // Vardiya süresini alalım
                        var vardiyaSuresi = TimeSpan.Zero;
                        var vardiya = vardiyaListesi.FirstOrDefault(); // Varsayılan vardiya bilgisi alınıyor
                        if (vardiya != null)
                        {
                            vardiyaSuresi = vardiya.CalismaBitis - vardiya.CalismaBaslangic;
                        }

                        // Eksik çalışma süresi hesapla
                        if (vardiyaSuresi > calismaSuresiTimeSpan)
                        {
                            var eksikSure = vardiyaSuresi - calismaSuresiTimeSpan;
                            eksikSureText = eksikSure.ToString(@"hh\:mm");
                        }

                        // Fazla mesai hesapla
                        if (calismaSuresiTimeSpan > vardiyaSuresi)
                        {
                            var fazlaMesai = calismaSuresiTimeSpan - vardiyaSuresi;
                            fazlaMesaiText = fazlaMesai.ToString(@"hh\:mm");
                        }
                    }
                    else
                    {
                        calismaSuresi = "Veri Eksik";
                    }
                }
                else
                {
                    // Eksik veriler varsa
                    calismaSuresi = "Veri Eksik";
                }

                AddStyledCell(table, calismaSuresi, font, backgroundColor);
                AddStyledCell(table, eksikSureText, font, backgroundColor);
                AddStyledCell(table, fazlaMesaiText, font, backgroundColor);
            }

            pdfDoc.Add(table);
            pdfDoc.Close();

            return stream.ToArray(); // PDF verisini
        }
    }

    private void AddStyledCell(PdfPTable table, string text, Font font, BaseColor backgroundColor)
    {
        var cell = new PdfPCell(new Phrase(text, font))
        {
            VerticalAlignment = Element.ALIGN_MIDDLE,
            HorizontalAlignment = Element.ALIGN_LEFT,
            Padding = 5,
            BackgroundColor = backgroundColor,
            BorderColor = BaseColor.GRAY,
            BorderWidth = 1
        };
        table.AddCell(cell);
    }

    private void AddStyledCell(PdfPTable table, string text, Font font, bool isHeader = false)
    {
        var cell = new PdfPCell(new Phrase(text, font))
        {
            VerticalAlignment = Element.ALIGN_MIDDLE,
            HorizontalAlignment = isHeader ? Element.ALIGN_CENTER : Element.ALIGN_LEFT,
            Padding = 5,
            BackgroundColor = isHeader ? BaseColor.LIGHT_GRAY : BaseColor.WHITE,
            BorderColor = BaseColor.GRAY,
            BorderWidth = 1
        };
        table.AddCell(cell);
    }
    private HareketViewModel HareketDegerlendirmeReport(DateTime? StartDate, DateTime? EndDate)
    {
        DateTime startDate = DateTime.Now.Date.AddDays(-1); // Dün
        DateTime endDate = DateTime.Now.Date; // Bugün
        endDate = endDate.AddHours(23).AddMinutes(59).AddSeconds(59).AddMilliseconds(999); // Bugünün sonu

        var model = new HareketViewModel
        {
            PersonelListesi = GetPersonelListesi(),
            HareketListesi = GetHareketListesi(),
            StartDate = startDate,
            EndDate = endDate
        };

        var hareketListesi = model.HareketListesi
            .Where(h => h.Tarih >= startDate && h.Tarih <= endDate)
            .ToList();

        var groupedHareketListesi = model.PersonelListesi
            .SelectMany(personel => hareketListesi
                .Where(h => h.PersonelId == personel.id)
                .GroupBy(h => h.Tarih)
                .Select(g =>
                {
                    var hareket = new Hareket
                    {
                        Tarih = g.Key,
                        PersonelId = personel.id,
                        personelAdi = personel.adi,
                        personelSoyadi = personel.soyadi,
                        GirişSaatleri = g.Where(h => h.IslemTipi == "01")
                                        .OrderBy(h => h.Saat)
                                        .Select(h => h.Saat)
                                        .ToList(),
                        ÇıkışSaatleri = g.Where(h => h.IslemTipi == "02")
                                        .OrderByDescending(h => h.Saat)
                                        .Select(h => h.Saat)
                                        .ToList(),
                        Bilgi = !g.Any() // Eğer hiçbir hareket yoksa "DEVAMSIZ"
                        ? "DEVAMSIZ"
                        : g.Any(h => h.IslemTipi == "01") && g.Any(h => h.IslemTipi == "02")
                        ? "OK"
                        : g.Any(h => h.IslemTipi == "01")
                        ? "ÇIKIŞ YOK"
                        : g.Any(h => h.IslemTipi == "02")
                        ? "GİRİŞ YOK"
                        : "DEVAMSIZ",
                        GirişDurumu = ""
                    };

                    var vardiya = GetVardiyaById(personel.VardiyaId);
                    if (vardiya != null)
                    {
                        TimeSpan erkenGelmeSaati = vardiya.ErkenGelme;
                        TimeSpan gecGelmeSaati = vardiya.GecGelme;
                        var girisSaati = hareket.GirişSaatleri.FirstOrDefault();
                        if (girisSaati != null)
                        {
                            if (girisSaati < erkenGelmeSaati)
                            {
                                hareket.GirişDurumu = "Erken";
                            }
                            else if (girisSaati > gecGelmeSaati)
                            {
                                hareket.GirişDurumu = "Geç";
                            }
                            else if (hareket.Bilgi == "DEVAMSIZ")
                            {
                                hareket.GirişDurumu = "DEVAMSIZ";
                            }
                            else
                            {
                                hareket.GirişDurumu = "OK";
                            }
                        }
                    }

                    return hareket;
                })
            )
            .ToList();

        // Devamsız olan personelleri ekle
        var devamsizPersoneller = model.PersonelListesi
            .SelectMany(p => new List<Hareket>
            {
            new Hareket
            {
                Tarih = startDate,
                PersonelId = p.id,
                personelAdi = p.adi,
                personelSoyadi = p.soyadi,
                GirişSaatleri = new List<TimeSpan>(),
                ÇıkışSaatleri = new List<TimeSpan>(),
                Bilgi = !groupedHareketListesi.Any(h => h.PersonelId == p.id && h.Tarih == startDate) ? "DEVAMSIZ" : "OK",
                GirişDurumu = !groupedHareketListesi.Any(h => h.PersonelId == p.id && h.Tarih == startDate) ? "DEVAMSIZ" : ""
            },
            new Hareket
            {
                Tarih = endDate.Date,
                PersonelId = p.id,
                personelAdi = p.adi,
                personelSoyadi = p.soyadi,
                GirişSaatleri = new List<TimeSpan>(),
                ÇıkışSaatleri = new List<TimeSpan>(),
                Bilgi = !groupedHareketListesi.Any(h => h.PersonelId == p.id && h.Tarih == endDate.Date) ? "DEVAMSIZ" : "OK",
                GirişDurumu = !groupedHareketListesi.Any(h => h.PersonelId == p.id && h.Tarih == endDate.Date) ? "DEVAMSIZ" : ""
            }
            })
            .Where(h => h.Bilgi == "DEVAMSIZ") // Sadece devamsız olanları ekle
            .ToList();

        model.HareketListesi = groupedHareketListesi.Concat(devamsizPersoneller).OrderBy(h => h.PersonelId).ThenBy(h => h.Tarih).ToList();

        return model;
    }
    private List<personel> GetPersonelListesi()
    {
        using (var db = new YillikizinEntities())
        {
            return db.personel.ToList();
        }
    }

    private List<Hareket> GetHareketListesi()
    {
        using (var context = new YillikizinEntities())
        {
            var hareketListesi = context.Hareketler
                .Select(h => new Hareket
                {
                    id = h.Id,
                    Tarih = h.Tarih ?? DateTime.MinValue,
                    Saat = h.Saat ?? TimeSpan.Zero,
                    TerminalNo = h.TerminalNo,
                    PersonelId = h.PersonelId ?? 0,
                    IslemTipi = h.Yon,
                    Bilgi = h.Bilgi
                })
                .OrderBy(h => h.Tarih) // Tarihe göre sıralama
                .ThenBy(h => h.Saat)  // Saate göre sıralama
                .ToList();

            return hareketListesi;
        }
    }
    private List<Vardiya> GetVardiyaListesi()
    {
        // VardiyaListesi'ni veritabanından alın
        return db.Vardiya.ToList();
    }
    private Vardiya GetVardiyaById(int? vardiyaId)
    {
        if (!vardiyaId.HasValue)
        {
            return null;
        }

        using (var context = new YillikizinEntities())
        {
            return context.Vardiya.FirstOrDefault(v => v.VardiyaId == vardiyaId.Value);
        }
    }
}