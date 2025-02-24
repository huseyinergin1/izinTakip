using ClosedXML.Excel;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using yillikizin.Filters;
using yillikizin.Models;
[CustomAuthorize]
public class IzinController : Controller
{
    private YillikizinEntities db = new YillikizinEntities(); // Veritabanı bağlantısı

    // İzin ve Personel listelerini gösterecek ana sayfa
    public ActionResult Index()
    {
        var today = DateTime.Today;

        // Fetch current and past leave records
        var currentIzinList = db.Izin.Include(i => i.personel)
                                       .Where(i => i.BitisTarihi >= today)
                                       .ToList();

        var pastIzinList = db.Izin.Include(i => i.personel)
                                    .Where(i => i.BitisTarihi < today)
                                    .ToList();

        // Create the view model and populate it
        var viewModel = new IzinViewModel
        {
            CurrentIzinList = currentIzinList,
            PastIzinList = pastIzinList
        };

        return View(viewModel);
    }
    public ActionResult IzinBilgileri()
    {
        var personelListesi = db.personel.ToList();

        foreach (var personel in personelListesi)
        {
            // İlgili personelin sadece "Yıllık İzin" türündeki izin günlerini hesapla
            var kullanilanIzin = db.Izin
                .Where(i => i.Personelıd == personel.id && i.IzinTuru == "YILLIK İZİN") // İzin türünü kontrol et
                .Sum(i => DbFunctions.DiffDays(i.BaslangicTarihi, i.BitisTarihi)) ?? 0;

            // Hesaplanan kullanılmış izin gün sayısını ve kalan izin gün sayısını atıyoruz
            personel.kullandigi = kullanilanIzin;
            personel.kalan = personel.hakettigi - kullanilanIzin;
        }

        return View(personelListesi); // Model olarak personel listesini gönderiyoruz
    }

    public JsonResult GetIzinData(DateTime startDate, DateTime endDate)
    {
        var izinListesi = db.Izin
            .Where(i => DbFunctions.TruncateTime(i.BaslangicTarihi) >= startDate.Date &&
                        DbFunctions.TruncateTime(i.BitisTarihi) <= endDate.Date)
            .Select(i => new
            {
                i.Personelıd,
                i.BaslangicTarihi,
                i.BitisTarihi,
                i.IzinTuru
            })
            .ToList();

        return Json(izinListesi, JsonRequestBehavior.AllowGet);
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public JsonResult GuncelleIzin(int personelId, int hakettigi)
    {
        try
        {
            var personel = db.personel.Find(personelId);
            if (personel == null)
            {
                return Json(new { success = false, message = "Personel bulunamadı." });
            }

            personel.hakettigi = hakettigi;
            db.SaveChanges();

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            // Hata mesajını günlüğe yazdır
            System.Diagnostics.Debug.WriteLine(ex.Message);
            return Json(new { success = false, message = ex.Message });
        }
    }
    public ActionResult ExportToExcel()
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

        using (var package = new OfficeOpenXml.ExcelPackage())
        {
            var worksheet = package.Workbook.Worksheets.Add("Izin Bilgileri");
            worksheet.Cells[1, 1].Value = "Adı";
            worksheet.Cells[1, 2].Value = "Soyadı";
            worksheet.Cells[1, 3].Value = "Kart No";
            worksheet.Cells[1, 4].Value = "İşe Giriş Tarihi";
            worksheet.Cells[1, 5].Value = "Hak Edilen İzin (Gün)";
            worksheet.Cells[1, 6].Value = "Kullandığı İzin (Gün)";
            worksheet.Cells[1, 7].Value = "Kalan İzin (Gün)";

            for (int i = 0; i < izinBilgileri.Count; i++)
            {
                worksheet.Cells[i + 2, 1].Value = izinBilgileri[i].adi;
                worksheet.Cells[i + 2, 2].Value = izinBilgileri[i].soyadi;
                worksheet.Cells[i + 2, 3].Value = izinBilgileri[i].kartno;
                worksheet.Cells[i + 2, 4].Value = DateTime.Parse(izinBilgileri[i].isegiristarih.ToString()).ToString("dd.MM.yyyy");
                worksheet.Cells[i + 2, 5].Value = izinBilgileri[i].hakettigi;
                worksheet.Cells[i + 2, 6].Value = izinBilgileri[i].kullandigi;
                worksheet.Cells[i + 2, 7].Value = izinBilgileri[i].kalan;
            }

            var stream = new System.IO.MemoryStream();
            package.SaveAs(stream);
            var content = stream.ToArray();
            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "IzinBilgileri.xlsx");
        }
    }
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
            var document = new iTextSharp.text.Document(iTextSharp.text.PageSize.A4, 50, 50, 25, 25);
            var writer = iTextSharp.text.pdf.PdfWriter.GetInstance(document, stream);
            document.Open();

            var baseFont = iTextSharp.text.pdf.BaseFont.CreateFont("C:/windows/fonts/arial.ttf", "Identity-H", iTextSharp.text.pdf.BaseFont.EMBEDDED);
            var titleFont = new iTextSharp.text.Font(baseFont, 18, iTextSharp.text.Font.BOLD, BaseColor.BLACK);
            var title = new iTextSharp.text.Paragraph("İzin Bilgileri Raporu", titleFont)
            {
                Alignment = iTextSharp.text.Element.ALIGN_CENTER,
                SpacingAfter = 20f
            };
            document.Add(title);

            var dateFont = new iTextSharp.text.Font(baseFont, 12, iTextSharp.text.Font.NORMAL, BaseColor.BLACK);
            var date = new iTextSharp.text.Paragraph($"Tarih: {DateTime.Now.ToString("dd.MM.yyyy")}", dateFont)
            {
                Alignment = iTextSharp.text.Element.ALIGN_RIGHT,
                SpacingAfter = 20f
            };
            document.Add(date);

            var table = new iTextSharp.text.pdf.PdfPTable(7)
            {
                WidthPercentage = 100,
                SpacingBefore = 10f,
                SpacingAfter = 10f
            };

            table.SetWidths(new float[] { 3f, 2.5f, 2f, 2.5f, 2f, 2f, 2f });

            var headerFont = new iTextSharp.text.Font(baseFont, 12, iTextSharp.text.Font.BOLD, BaseColor.BLACK);
            var cellFont = new iTextSharp.text.Font(baseFont, 10, iTextSharp.text.Font.NORMAL, BaseColor.BLACK);

            table.AddCell(new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase("Adı", headerFont)) { HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER });
            table.AddCell(new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase("Soyadı", headerFont)) { HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER });
            table.AddCell(new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase("Kart No", headerFont)) { HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER });
            table.AddCell(new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase("İşe Giriş Tarihi", headerFont)) { HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER });
            table.AddCell(new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase("Hak Edilen", headerFont)) { HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER });
            table.AddCell(new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase("Kullandığı", headerFont)) { HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER });
            table.AddCell(new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase("Kalan", headerFont)) { HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER });

            foreach (var item in izinBilgileri)
            {
                var iseGirisTarihi = DateTime.Parse(item.isegiristarih.ToString());

                table.AddCell(new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase(item.adi, cellFont)) { HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER });
                table.AddCell(new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase(item.soyadi, cellFont)) { HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER });
                table.AddCell(new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase(item.kartno, cellFont)) { HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER });
                table.AddCell(new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase(iseGirisTarihi.ToString("dd.MM.yyyy"), cellFont)) { HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER });
                table.AddCell(new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase(item.hakettigi.ToString(), cellFont)) { HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER });
                table.AddCell(new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase(item.kullandigi.ToString(), cellFont)) { HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER });
                table.AddCell(new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase(item.kalan.ToString(), cellFont)) { HorizontalAlignment = iTextSharp.text.Element.ALIGN_CENTER });
            }

            document.Add(table);
            document.Close();

            return File(stream.ToArray(), "application/pdf", "IzinBilgileri.pdf");
        }
    }

    // İzin ekleme sayfasını göster
    public ActionResult IzinEkle()
    {
        // Tüm personel listesini çekiyoruz
        var personelListesi = db.personel.ToList();

        // İzin türlerini çekiyoruz
        var izinTurleri = db.IzinTuru.ToList();
        ViewBag.IzinTurleri = new SelectList(izinTurleri, "IzinTuruId", "IzinTuruAdi"); // İzin türlerini dropdown için ayarlıyoruz

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
    public ActionResult IzinEkle(List<int> selectedPersonelIds, DateTime baslangicTarihi, DateTime bitisTarihi, int izinTuruId, string aciklama)
    {
        if (selectedPersonelIds != null && selectedPersonelIds.Count > 0)
        {
            var izinTuru = db.IzinTuru.Find(izinTuruId);
            string izinTuruAdi = izinTuru?.IzinTuruAdi;

            foreach (var personelId in selectedPersonelIds)
            {
                var yeniIzin = new Izin
                {
                    Personelıd = personelId,
                    BaslangicTarihi = baslangicTarihi,
                    BitisTarihi = bitisTarihi,
                    IzinTuru = izinTuruAdi,
                    Aciklama = aciklama,
                };

                db.Izin.Add(yeniIzin);
            }

            try
            {
                db.SaveChanges();
                ViewBag.Message = "İzin başarıyla tanımlandı!";
            }
            catch (Exception ex)
            {
                ViewBag.Message = "Hata: " + ex.Message;
            }
        }
        else
        {
            ViewBag.Message = "Lütfen en az bir personel seçin.";
        }

        var personelListesi = db.personel.ToList();
        ViewBag.IzinTurleri = new SelectList(db.IzinTuru.ToList(), "IzinTuruId", "IzinTuruAdi");
        return View(personelListesi);
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
    [HttpPost]
    public JsonResult DeleteSelectedIzin(List<int> ids)
    {
        bool success = false;
        string message = string.Empty;

        if (ids != null && ids.Count > 0)
        {
            var izinler = db.Izin.Where(i => ids.Contains(i.IzinId)).ToList();
            if (izinler.Any())
            {
                db.Izin.RemoveRange(izinler);
                db.SaveChanges();
                success = true;
                message = "Seçilen izinler başarıyla silindi!";
            }
            else
            {
                message = "Seçilen izinler bulunamadı.";
            }
        }
        else
        {
            message = "Lütfen silmek için en az bir izin seçin.";
        }

        return Json(new { success = success, message = message });
    }
    [HttpPost]
    public ActionResult UpdateIzin(int id, DateTime baslangicTarihi, DateTime bitisTarihi, string aciklama)
    {
        try
        {
            var izin = db.Izin.Find(id);
            if (izin != null)
            {
                izin.BaslangicTarihi = baslangicTarihi;
                izin.BitisTarihi = bitisTarihi;
                izin.Aciklama = aciklama;

                db.Entry(izin).State = EntityState.Modified;
                db.SaveChanges();
            }
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public ActionResult DeleteIzin(int id)
    {
        try
        {
            var izin = db.Izin.Find(id);
            if (izin != null)
            {
                db.Izin.Remove(izin);
                db.SaveChanges();
                return Json(new { success = true, message = "İzin başarıyla silindi!" });
            }
            else
            {
                return Json(new { success = false, message = "İzin kaydı bulunamadı." });
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Hata: " + ex.Message });
        }
    }

    public ActionResult IzinTuru()
    {
        ViewBag.Title = "Yıllık İzin | 2024";

        // Tüm departmanları listele
        var izinTurleri = db.IzinTuru.ToList();
        return View(izinTurleri);
    }

    // Departman Ekleme İşlemi (POST)
    [HttpPost]
    public ActionResult IzinTuruEkle(string IzinTuruAdi)
    {
        if (!string.IsNullOrEmpty(IzinTuruAdi))
        {
            // Yeni departmanı oluştur
            IzinTuru yeniIzinTuru = new IzinTuru
            {
                IzinTuruAdi = IzinTuruAdi
            };

            // Veritabanına ekle
            db.IzinTuru.Add(yeniIzinTuru);
            db.SaveChanges();

            // Başarılı ekleme sonrası aynı sayfaya geri dön
            return RedirectToAction("IzinTuru");
        }

        // Departman adı boşsa hata mesajı ekleyip formu tekrar göster
        ModelState.AddModelError("", "Departman adı boş olamaz.");
        return View("IzinTuru");
    }

}

