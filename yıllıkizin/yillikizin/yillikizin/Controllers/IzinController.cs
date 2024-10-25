using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using yillikizin.Models;

public class IzinController : Controller
{
    private YillikizinEntities db = new YillikizinEntities(); // Veritabanı bağlantısı

    // İzin ve Personel listelerini gösterecek ana sayfa
    public ActionResult Index()
    {
        var izinListesi = db.Izin.Include(i => i.personel).ToList(); // İzinleri ve ilişkili personelleri çek
        return View(izinListesi); // Model'i View'a gönder
    }

    public ActionResult IzinBilgileri()
    {
        var personelListesi = db.personel.ToList();

        foreach (var personel in personelListesi)
        {
            var kullanilanIzin = db.Izin.Where(i => i.Personelıd == personel.id)
                                        .Sum(i => DbFunctions.DiffDays(i.BaslangicTarihi, i.BitisTarihi)) ?? 0;

            personel.kullandigi = kullanilanIzin; // Kullandığı izin gün sayısını atıyoruz
            personel.kalan = personel.hakettigi - kullanilanIzin; // Kalan izin gün sayısını hesaplıyoruz
        }

        return View(personelListesi); // Model olarak personel listesini gönderiyoruz
    }


    // EXCEL
    [HttpPost]
    public ActionResult ExportToExcel()
    {
        var izinBilgileri = db.personel.Select(p => new
        {
            p.adi,
            p.soyadi,  // Soyadı eklendi
            p.kartno,  // Kart No eklendi
            p.isegiristarih,  // İşe Giriş Tarihi eklendi
            p.hakettigi,
            p.kullandigi,
            p.kalan
        }).ToList();

        using (var workbook = new ClosedXML.Excel.XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add("Izin Bilgileri");
            worksheet.Cell(1, 1).Value = "Adı Soyadı";
            worksheet.Cell(1, 2).Value = "Kart No";
            worksheet.Cell(1, 3).Value = "İşe Giriş Tarihi";
            worksheet.Cell(1, 4).Value = "Hak Edilen İzin (Gün)";
            worksheet.Cell(1, 5).Value = "Kullandığı İzin (Gün)";
            worksheet.Cell(1, 6).Value = "Kalan İzin (Gün)";

            for (int i = 0; i < izinBilgileri.Count; i++)
            {
                worksheet.Cell(i + 2, 1).Value = izinBilgileri[i].adi + " " + izinBilgileri[i].soyadi;
                worksheet.Cell(i + 2, 2).Value = izinBilgileri[i].kartno;
                worksheet.Cell(i + 2, 3).Value = izinBilgileri[i].isegiristarih;
                worksheet.Cell(i + 2, 4).Value = izinBilgileri[i].hakettigi;
                worksheet.Cell(i + 2, 5).Value = izinBilgileri[i].kullandigi;
                worksheet.Cell(i + 2, 6).Value = izinBilgileri[i].kalan;
            }

            using (var stream = new System.IO.MemoryStream())
            {
                workbook.SaveAs(stream);
                var content = stream.ToArray();
                return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "IzinBilgileri.xlsx");
            }
        }
    }

    // PDF
    [HttpPost]
    public ActionResult ExportToPdf()
    {
        var izinBilgileri = db.personel.Select(p => new
        {
            p.adi,
            p.soyadi,
            p.kartno,
            p.isegiristarih,
            p.hakettigi,
            p.kullandigi,
            p.kalan
        }).ToList();

        using (var stream = new System.IO.MemoryStream())
        {
            var pdfDoc = new iTextSharp.text.Document(iTextSharp.text.PageSize.A4, 25, 25, 30, 30);
            var writer = iTextSharp.text.pdf.PdfWriter.GetInstance(pdfDoc, stream);
            pdfDoc.Open();

            // Arial Unicode MS fontunu kullanarak Türkçe karakter desteği sağlanır
            string arialUnicodeFontPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "ARIALUNI.TTF");
            var baseFont = iTextSharp.text.pdf.BaseFont.CreateFont(arialUnicodeFontPath, iTextSharp.text.pdf.BaseFont.IDENTITY_H, iTextSharp.text.pdf.BaseFont.EMBEDDED);
            var font = new iTextSharp.text.Font(baseFont, 12, iTextSharp.text.Font.NORMAL);

            // Başlık ekleyelim
            var titleFont = new iTextSharp.text.Font(baseFont, 16, iTextSharp.text.Font.BOLD);
            var title = new iTextSharp.text.Paragraph("Personel İzin Bilgileri", titleFont);
            title.Alignment = iTextSharp.text.Element.ALIGN_CENTER;
            title.SpacingAfter = 20;
            pdfDoc.Add(title);

            // PDF Tablosu oluşturma
            var table = new iTextSharp.text.pdf.PdfPTable(6) { WidthPercentage = 100 };
            table.SetWidths(new float[] { 2, 2, 2, 2, 2, 2 }); // Kolon genişlikleri ayarlanıyor

            // Başlıklar
            table.AddCell(new iTextSharp.text.Phrase("Adı Soyadı", font));
            table.AddCell(new iTextSharp.text.Phrase("Kart No", font));
            table.AddCell(new iTextSharp.text.Phrase("İşe Giriş Tarihi", font));
            table.AddCell(new iTextSharp.text.Phrase("Hak Edilen İzin (Gün)", font));
            table.AddCell(new iTextSharp.text.Phrase("Kullandığı İzin (Gün)", font));
            table.AddCell(new iTextSharp.text.Phrase("Kalan İzin (Gün)", font));

            // Veri doldurma
            foreach (var izin in izinBilgileri)
            {
                table.AddCell(new iTextSharp.text.Phrase(izin.adi + " " + izin.soyadi, font)); // Adı Soyadı
                table.AddCell(new iTextSharp.text.Phrase(izin.kartno?.ToString() ?? "", font)); // Kart No
                table.AddCell(new iTextSharp.text.Phrase(izin.isegiristarih?.ToString("dd/MM/yyyy") ?? "", font)); // İşe Giriş Tarihi
                table.AddCell(new iTextSharp.text.Phrase(izin.hakettigi.ToString(), font)); // Hak Edilen İzin
                table.AddCell(new iTextSharp.text.Phrase(izin.kullandigi.ToString(), font)); // Kullandığı İzin
                table.AddCell(new iTextSharp.text.Phrase(izin.kalan.ToString(), font)); // Kalan İzin
            }

            // Tabloyu PDF'e ekleyelim
            pdfDoc.Add(table);
            pdfDoc.Close();

            // PDF dosyasını geri döndürelim
            return File(stream.ToArray(), "application/pdf", "IzinBilgileri.pdf");
        }
    }

    // İzin ekleme sayfasını göster
    public ActionResult IzinEkle()
    {
        // Tüm personel listesini çekiyoruz
        var personelListesi = db.personel.ToList(); // Tüm personel bilgilerini alıyoruz

        // Her personelin gerekli bilgilerini içeren bir liste oluşturuyoruz
        var model = personelListesi.Select(p => new yillikizin.Models.personel
        {
            id = p.id,
            adi = p.adi,
            soyadi = p.soyadi,
            isegiristarih = p.isegiristarih,
            kartno = p.kartno,
            hakettigi = p.hakettigi,
            kullandigi = db.Izin.Where(i => i.Personelıd == p.id)
                                .Sum(i => DbFunctions.DiffDays(i.BaslangicTarihi, i.BitisTarihi)) ?? 0,
            kalan = p.hakettigi - (db.Izin.Where(i => i.Personelıd == p.id)
                                             .Sum(i => DbFunctions.DiffDays(i.BaslangicTarihi, i.BitisTarihi)) ?? 0)
        }).ToList();

        return View(model); // Model olarak personel listesi gönderiliyor
    }


    [HttpPost]
    public ActionResult IzinEkle(List<int> selectedPersonelIds, DateTime baslangicTarihi, DateTime bitisTarihi)
    {
        if (selectedPersonelIds != null && selectedPersonelIds.Count > 0)
        {
            foreach (var personelId in selectedPersonelIds)
            {
                // Yeni izin oluşturuluyor
                var yeniIzin = new Izin
                {
                    Personelıd = personelId,
                    BaslangicTarihi = baslangicTarihi,
                    BitisTarihi = bitisTarihi,
                    IzinTuru = "Yıllık İzin",
                    Aciklama = "Yıllık izin tanımlandı"
                };

                db.Izin.Add(yeniIzin); // İzin veritabanına ekleniyor
            }

            db.SaveChanges(); // Değişiklikler kaydediliyor
            ViewBag.Message = "İzin başarıyla tanımlandı!";
        }
        else
        {
            ViewBag.Message = "Lütfen en az bir personel seçin.";
        }

        // Tüm personel listesini tekrar çekiyoruz
        var personelListesi = db.personel.ToList();
        return View(personelListesi); // Model'i tekrar View'a gönderiyoruz
    }

    [HttpPost]
    public ActionResult IzinSil(List<int> selectedPersonelIds, DateTime baslangicTarihi, DateTime bitisTarihi)
    {
        if (selectedPersonelIds != null && selectedPersonelIds.Count > 0)
        {
            foreach (var personelId in selectedPersonelIds)
            {
                // Belirtilen tarihler arasında seçilen personelin izinlerini siliyoruz
                var izinler = db.Izin.Where(i => i.Personelıd == personelId &&
                                                 DbFunctions.TruncateTime(i.BaslangicTarihi) >= baslangicTarihi.Date &&
                                                 DbFunctions.TruncateTime(i.BitisTarihi) <= bitisTarihi.Date)
                                     .ToList();

                if (izinler.Any())
                {
                    db.Izin.RemoveRange(izinler); // İzinleri veritabanından siliyoruz
                }
            }

            db.SaveChanges(); // Değişiklikler kaydediliyor
            ViewBag.Message = "İzinler başarıyla silindi!";
        }
        else
        {
            ViewBag.Message = "Lütfen en az bir personel seçin.";
        }

        // Tüm personel listesini tekrar çekiyoruz
        var personelListesi = db.personel.ToList();
        return View("IzinEkle", personelListesi); // Model'i tekrar IzinEkle View'ına gönderiyoruz
    }
}
