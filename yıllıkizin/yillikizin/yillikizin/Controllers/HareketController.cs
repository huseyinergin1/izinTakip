using ClosedXML.Excel;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using yillikizin.Models;
using OfficeOpenXml;
using yillikizin.Filters;
using System.Drawing;
using OfficeOpenXml.Style;
using yillikizin.Controllers;
using iTextSharp.text.pdf;
using iTextSharp.text;


[OzelYetki]
public class HareketController : Controller
{
    private YillikizinEntities db = new YillikizinEntities();
    // Personel listesi sayfası
    // GET: Home
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
    [OzelYetki]
    public ActionResult HareketDegerlendirme(DateTime? StartDate, DateTime? EndDate, int? SelectedPersonelId, string FilterType)
    {
        // VardiyaListesi'ni al
        var vardiyaListesi = GetVardiyaListesi();

        DateTime startDate = StartDate ?? DateTime.Now.Date;
        DateTime endDate = EndDate ?? DateTime.Now.Date;
        endDate = endDate.AddHours(23).AddMinutes(59).AddSeconds(59).AddMilliseconds(999);

        // Sadece çalışma durumu true olan personelleri al
        var aktifPersoneller = GetPersonelListesi().Where(p => p.calisma == true).ToList();

        var model = new HareketViewModel
        {
            PersonelListesi = aktifPersoneller,
            HareketListesi = GetHareketListesi(),
            IzinListesi = GetIzinListesi(),
            VardiyaListesi = vardiyaListesi,
            SelectedPersonelId = SelectedPersonelId,
            StartDate = startDate,
            EndDate = endDate
        };

        // Tarih filtresi uygula
        model.HareketListesi = model.HareketListesi
            .Where(h => h.Tarih >= startDate && h.Tarih <= endDate)
            .ToList();

        // Sadece aktif personellerin hareketlerini filtrele
        model.HareketListesi = model.HareketListesi
            .Where(h => aktifPersoneller.Any(p => p.kartno == h.kartno))
            .ToList();

        // Personel filtresi uygula
        if (SelectedPersonelId.HasValue)
        {
            model.HareketListesi = model.HareketListesi
                .Where(h => h.PersonelId == SelectedPersonelId.Value)
                .ToList();
        }

        // Hareketleri grupla ve işle
        model.HareketListesi = model.HareketListesi
            .GroupBy(h => new { h.Tarih, h.kartno })
            .Select(g =>
            {
                var hareket = new Hareket
                {
                    Tarih = g.Key.Tarih,
                    kartno = g.Key.kartno,
                    personelAdi = g.First().personelAdi,
                    personelSoyadi = g.First().personelSoyadi,
                    GirişSaatleri = g.Where(h => h.IslemTipi == "01")
                                     .OrderBy(h => h.Saat)
                                     .Select(h => h.Saat)
                                     .ToList(),
                    ÇıkışSaatleri = g.Where(h => h.IslemTipi == "02")
                                     .OrderByDescending(h => h.Saat)
                                     .Select(h => h.Saat)
                                     .ToList(),
                    Bilgi = !g.Any() ? "DEVAMSIZ" :
                            g.Any(h => h.IslemTipi == "01") && g.Any(h => h.IslemTipi == "02") ? "OK" :
                            g.Any(h => h.IslemTipi == "01") ? "ÇIKIŞ YOK" :
                            "GİRİŞ YOK"
                };

                var personel = aktifPersoneller.FirstOrDefault(p => p.kartno == hareket.kartno);
                if (personel != null)
                {
                    hareket.PersonelId = personel.id;
                    var vardiya = GetVardiyaById(personel.VardiyaId);
                    if (vardiya != null)
                    {
                        var girisSaati = hareket.GirişSaatleri.FirstOrDefault();
                        var cikisSaati = hareket.ÇıkışSaatleri?.FirstOrDefault();

                        // Giriş durumu kontrolü
                        if (girisSaati != null && girisSaati.Hours > 0)
                        {
                            if (hareket.Bilgi == "DEVAMSIZ")
                            {
                                hareket.GirişDurumu = "DEVAMSIZ";
                            }
                            else if (girisSaati < vardiya.ErkenGelme)
                            {
                                hareket.GirişDurumu = "Erken";
                            }
                            else if (girisSaati > vardiya.GecGelme)
                            {
                                hareket.GirişDurumu = "Geç";
                            }
                            else
                            {
                                hareket.GirişDurumu = "OK";
                            }
                        }

                        // Çıkış durumu kontrolü
                        if (cikisSaati != null)
                        {
                            if (cikisSaati < vardiya.ErkenCikma)
                            {
                                hareket.ÇıkışDurumu = "ErkenÇıkma";
                            }
                            else
                            {
                                hareket.ÇıkışDurumu = "Normal";
                            }
                        }

                        // Çalışma süresi ve fazla mesai hesaplama
                        if (girisSaati != null && cikisSaati != null)
                        {
                            var vardiyaBaslangic = vardiya.CalismaBaslangic;
                            var vardiyaBitis = vardiya.CalismaBitis;
                            var fazlaMesaiBaslangic = vardiyaBitis.Add(TimeSpan.FromMinutes(45)); // Fazla mesai başlangıç saatini hesapla

                            // Normal çalışma süresi hesaplama
                            TimeSpan girisSaatiValue = (TimeSpan)girisSaati;
                            TimeSpan cikisSaatiValue = (TimeSpan)cikisSaati;

                            // Başlangıç saatini vardiya başlangıcından erken ise vardiya başlangıcı olarak al
                            var baslangicSaati = girisSaatiValue < vardiyaBaslangic ? vardiyaBaslangic : girisSaatiValue;
                            hareket.CalismaSuresi = cikisSaatiValue - baslangicSaati;

                            // Fazla mesai hesaplama
                            TimeSpan fazlaMesai = TimeSpan.Zero;
                            TimeSpan MCS = vardiyaBaslangic - vardiyaBitis;

                            // Vardiya sonrası fazla mesai (sadece vardiya bitişinden sonraki süreyi hesapla)
                            if (cikisSaatiValue > fazlaMesaiBaslangic) // Fazla mesai başlangıç saatini kontrol et
                            {
                                fazlaMesai = cikisSaatiValue - fazlaMesaiBaslangic;
                                if (fazlaMesai > TimeSpan.Zero)
                                {
                                    fazlaMesai = fazlaMesai.Add(TimeSpan.FromMinutes(45)); // 45 dakikayı ekle
                                }
                            }

                            hareket.FazlaMesai = fazlaMesai;
                            hareket.MCS = MCS;

                            // Eksik süre hesaplama
                            var vardiyaSuresi = vardiyaBitis - vardiyaBaslangic;

                            if (girisSaati != null && cikisSaati != TimeSpan.Zero)
                            {
                                var eksikSure = vardiyaSuresi > hareket.CalismaSuresi ? vardiyaSuresi - hareket.CalismaSuresi : TimeSpan.Zero;
                                hareket.EksikSure = eksikSure;
                            }
                            else
                            {
                                // Giriş veya çıkış saati yoksa eksik süreyi direkt vardiya süresi olarak ayarla
                                hareket.EksikSure = vardiyaSuresi;
                            }
                        }
                        else
                        {
                            // Giriş veya çıkış saati yoksa eksik süreyi vardiya süresi olarak ayarla
                            var vardiyaSuresi = vardiya.CalismaBitis - vardiya.CalismaBaslangic;
                            hareket.EksikSure = vardiyaSuresi;

                            // Eksik sürenin saat değil, süre olarak görünmesini sağla
                            if (hareket.EksikSure.TotalMinutes > 0)
                            {
                                hareket.EksikSure = TimeSpan.FromMinutes(hareket.EksikSure.TotalMinutes);
                            }
                        }
                    }
                }

                return hareket;
            })
            .ToList();

        ViewBag.StartDate = startDate.ToString("yyyy-MM-dd");
        ViewBag.EndDate = endDate.ToString("yyyy-MM-dd");
        ViewBag.FilterType = FilterType; // Filtreleme için ViewBag'e eklendi

        return View(model);
    }

    public ActionResult ExportToExcel(DateTime? StartDate, DateTime? EndDate, int? SelectedPersonelId, string FilterType)
    {
        // Arama değerlerini al
        var tarihArama = Request.QueryString["tarihArama"]?.ToLower() ?? "";
        var adiArama = Request.QueryString["adiArama"]?.ToLower() ?? "";
        var soyadiArama = Request.QueryString["soyadiArama"]?.ToLower() ?? "";
        var kartArama = Request.QueryString["kartArama"]?.ToLower() ?? "";
        var departmanArama = Request.QueryString["departmanArama"]?.ToLower() ?? "";

        DateTime startDate = StartDate ?? DateTime.Now.Date;
        DateTime endDate = EndDate ?? DateTime.Now.Date;
        endDate = endDate.AddHours(23).AddMinutes(59).AddSeconds(59).AddMilliseconds(999);

        var personelListesi = GetPersonelListesi().Where(p => p.calisma == true).ToList();
        var hareketListesi = GetHareketListesi();
        var vardiyaListesi = GetVardiyaListesi();
        var izinListesi = GetIzinListesi();

        var allDates = Enumerable.Range(0, (endDate - startDate).Days + 1)
                                .Select(offset => startDate.AddDays(offset))
                                .ToList();

        var filteredData = from personel in personelListesi
                           from tarih in allDates
                           let hareketler = hareketListesi.Where(h => h.Tarih.Date == tarih.Date && h.kartno == personel.kartno).ToList()
                           let izin = izinListesi.FirstOrDefault(i => i.Personelıd == personel.id &&
                                                                     i.BaslangicTarihi <= tarih &&
                                                                     i.BitisTarihi >= tarih)
                           let vardiya = vardiyaListesi.FirstOrDefault(v => v.VardiyaId == personel.VardiyaId)
                           where (string.IsNullOrEmpty(adiArama) || personel.adi.ToLower().Contains(adiArama)) &&
                                 (string.IsNullOrEmpty(soyadiArama) || personel.soyadi.ToLower().Contains(soyadiArama)) &&
                                 (string.IsNullOrEmpty(kartArama) || personel.kartno.ToLower().Contains(kartArama)) &&
                                 (string.IsNullOrEmpty(departmanArama) || personel.departman.ToLower().Contains(departmanArama)) &&
                                 (string.IsNullOrEmpty(tarihArama) || tarih.ToString("yyyy-MM-dd ddd").ToLower().Contains(tarihArama))
                           select new
                           {
                               Tarih = tarih,
                               PersonelId = personel.id,
                               Adi = personel.adi,
                               Soyadi = personel.soyadi,
                               KartNo = personel.kartno,
                               Departman = personel.departman,
                               GirişSaatleri = hareketler.Where(h => h.IslemTipi == "01")
                                 .OrderBy(h => h.Saat)
                                 .Select(h => h.Saat)
                                 .ToList(),
                               ÇıkışSaatleri = hareketler.Where(h => h.IslemTipi == "02")
                                 .OrderByDescending(h => h.Saat)
                                 .Select(h => h.Saat)
                                 .ToList(),
                               Bilgi = tarih.DayOfWeek == DayOfWeek.Sunday ? "HAFTA TATİLİ" :
                                       izin != null ? izin.IzinTuru :
                                       !hareketler.Any() ? "DEVAMSIZ" :
                                       hareketler.Any(h => h.IslemTipi == "01") && hareketler.Any(h => h.IslemTipi == "02") ? "OK" :
                                       hareketler.Any(h => h.IslemTipi == "01") ? "ÇIKIŞ YOK" : "GİRİŞ YOK",
                               GirişDurumu = !hareketler.Any(h => h.IslemTipi == "01") ? "" :
                                             vardiya == null ? "" :
                                             hareketler.Where(h => h.IslemTipi == "01")
                                                       .OrderBy(h => h.Saat)
                                                       .Select(h => h.Saat)
                                                       .FirstOrDefault() < vardiya.ErkenGelme ? "Erken" :
                                             hareketler.Where(h => h.IslemTipi == "01")
                                                       .OrderBy(h => h.Saat)
                                                       .Select(h => h.Saat)
                                                       .FirstOrDefault() > vardiya.GecGelme ? "Geç" : "OK",
                               Vardiya = vardiya,
                               IsPazar = tarih.DayOfWeek == DayOfWeek.Sunday,
                               Izin = izin
                           };

        if (!string.IsNullOrEmpty(FilterType))
        {
            switch (FilterType)
            {
                case "Erken":
                    filteredData = filteredData.Where(d =>
                        d.GirişSaatleri.Any() && // Giriş kaydı var mı?
                        d.GirişDurumu == "Erken" && // Giriş durumu "Erken" mi?
                        d.Bilgi != "DEVAMSIZ" && // Devamsız değil mi?
                        d.Bilgi != "HAFTA TATİLİ" && // Hafta tatili değil mi?
                        d.Izin == null // İzinli değil mi?
                    );
                    break;
                case "Geç":
                    filteredData = filteredData.Where(d =>
                        d.GirişSaatleri.Any() &&
                        d.GirişDurumu == "Geç" &&
                        d.Bilgi != "DEVAMSIZ" &&
                        d.Bilgi != "HAFTA TATİLİ" &&
                        d.Izin == null
                    );
                    break;
                case "Izinliler":
                    filteredData = filteredData.Where(d => d.Izin != null);
                    break;
                case "Devamsiz":
                    filteredData = filteredData.Where(d => d.Bilgi == "DEVAMSIZ");
                    break;
                case "FazlaMesai":
                    filteredData = filteredData.Where(d =>
                        d.ÇıkışSaatleri.Any() &&
                        d.ÇıkışSaatleri.First() > d.Vardiya.CalismaBitis.Add(TimeSpan.FromMinutes(45))
                    );
                    break;
            }
        }

        using (var package = new ExcelPackage())
        {
            var worksheet = package.Workbook.Worksheets.Add("Hareketler");
            // Başlıklardan önce ekleyin
            if (!string.IsNullOrEmpty(FilterType))
            {
                worksheet.Cells[1, 1].Value = "Filtre:";
                worksheet.Cells[1, 2].Value = FilterType switch
                {
                    "Erken" => "Erken Gelenler",
                    "Geç" => "Geç Gelenler",
                    "Izinliler" => "İzinliler",
                    "Devamsiz" => "Devamsızlar",
                    "FazlaMesai" => "Fazla Mesai Yapanlar",
                    _ => FilterType
                };

                // Stil ayarları
                worksheet.Cells[1, 1].Style.Font.Bold = true;
                worksheet.Cells[1, 1, 1, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;

                int startRow = 2; // 'startRow' değişkenini tanımla ve başlat
                worksheet.Cells[startRow, 6].Style.Font.Color.SetColor(System.Drawing.Color.LightBlue);

            }

            // Başlıklar
            var headers = new[] { "Tarih", "Adı", "Soyadı", "Kart No", "Departman", "Giriş", "Çıkış",
                              "NCS", "Eksik", "Mesai", "Bilgi" };
            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cells[1, i + 1].Value = headers[i];
                worksheet.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                worksheet.Cells[1, i + 1].Style.Font.Bold = true;
            }

            int row = 2; // Initialize the 'row' variable before using it
            worksheet.Cells[row, 6].Style.Font.Color.SetColor(System.Drawing.Color.LightGray);

            foreach (var data in filteredData)
            {
                var currentRow = worksheet.Cells[row, 1, row, 11];

                // Temel bilgiler
                worksheet.Cells[row, 1].Value = data.Tarih.ToString("yyyy-MM-dd ddd");
                worksheet.Cells[row, 2].Value = data.Adi;
                worksheet.Cells[row, 3].Value = data.Soyadi;
                worksheet.Cells[row, 4].Value = data.KartNo;
                worksheet.Cells[row, 5].Value = data.Departman;

                // Giriş saatleri
                string girisSaatleri = data.GirişSaatleri.Any() ?
                    string.Join(", ", data.GirişSaatleri.Select(s => s.ToString(@"hh\:mm"))) : "00:00";
                worksheet.Cells[row, 6].Value = girisSaatleri;
                if (!string.IsNullOrEmpty(girisSaatleri))
                {
                    if (data.GirişDurumu == "Erken")
                        worksheet.Cells[row, 6].Style.Font.Color.SetColor(System.Drawing.Color.Blue);
                    else if (data.GirişDurumu == "Geç")
                        worksheet.Cells[row, 6].Style.Font.Color.SetColor(System.Drawing.Color.Red);
                }

                // Çıkış saatleri
                worksheet.Cells[row, 7].Value = data.ÇıkışSaatleri.Any() ?
                    string.Join(", ", data.ÇıkışSaatleri.Select(s => s.ToString(@"hh\:mm"))) : "00:00";

                // Geç çıkışları kırmızıya boyama
                if (data.ÇıkışSaatleri.Any() && data.Vardiya != null)
                {
                    var enSonCikis = data.ÇıkışSaatleri.First();
                    if (enSonCikis > data.Vardiya.GecCikma)
                    {
                        worksheet.Cells[row, 6].Style.Font.Color.SetColor(System.Drawing.Color.Red);
                    }
                }

                // NCS (Normal Çalışma Süresi) hesaplama
                TimeSpan calismaSuresi = TimeSpan.Zero;
                if (data.GirişSaatleri.Any() && data.ÇıkışSaatleri.Any())
                {
                    // Vardiya ve fazla mesai hesaplamalarını burada yapacağız
                    var vardiyaBaslangic = data.Vardiya.CalismaBaslangic;
                    var vardiyaBitis = data.Vardiya.CalismaBitis;
                    var fazlaMesaiBaslangic = vardiyaBitis.Add(TimeSpan.FromMinutes(45)); // Fazla mesai başlangıç saatini hesapla

                    TimeSpan girisSaatiValue = data.GirişSaatleri.First();
                    TimeSpan cikisSaatiValue = data.ÇıkışSaatleri.First();

                    // Başlangıç saatini vardiya başlangıcından erken ise vardiya başlangıcı olarak al
                    var baslangicSaati = girisSaatiValue < vardiyaBaslangic ? vardiyaBaslangic : girisSaatiValue;
                    calismaSuresi = cikisSaatiValue - baslangicSaati;

                    // Fazla mesai hesaplama
                    TimeSpan fazlaMesai = TimeSpan.Zero;
                    TimeSpan MCS = vardiyaBaslangic - vardiyaBitis;

                    // Vardiya sonrası fazla mesai (sadece vardiya bitişinden sonraki süreyi hesapla)
                    if (cikisSaatiValue > fazlaMesaiBaslangic) // Fazla mesai başlangıç saatini kontrol et
                    {
                        fazlaMesai = cikisSaatiValue - fazlaMesaiBaslangic;
                        if (fazlaMesai > TimeSpan.Zero)
                        {
                            fazlaMesai = fazlaMesai.Add(TimeSpan.FromMinutes(45)); // 45 dakikayı ekle
                        }
                    }

                    worksheet.Cells[row, 10].Value = fazlaMesai.ToString(@"hh\:mm");
                    worksheet.Cells[row, 8].Value = calismaSuresi.ToString(@"hh\:mm");

                    // Eksik süre hesaplama
                    var vardiyaSuresi = vardiyaBitis - vardiyaBaslangic;

                    if (girisSaatiValue != TimeSpan.Zero && cikisSaatiValue != TimeSpan.Zero)
                    {
                        var eksikSure = vardiyaSuresi > calismaSuresi ? vardiyaSuresi - calismaSuresi : TimeSpan.Zero;
                        worksheet.Cells[row, 9].Value = eksikSure.ToString(@"hh\:mm");
                    }
                    else
                    {
                        worksheet.Cells[row, 9].Value = vardiyaSuresi.ToString(@"hh\:mm");
                    }
                }
                else
                {
                    worksheet.Cells[row, 8].Value = "00:00";
                    worksheet.Cells[row, 9].Value = "00:00";
                    worksheet.Cells[row, 10].Value = "00:00";
                }

                // Bilgi ve satır renklendirme
                worksheet.Cells[row, 11].Value = data.Bilgi;

                // Satır arkaplan rengi
                if (data.IsPazar)
                {
                    currentRow.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    currentRow.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightYellow);
                }
                else if (data.Izin != null)
                {
                    currentRow.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    currentRow.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGreen);
                }
                else if (data.Bilgi == "DEVAMSIZ")
                {
                    currentRow.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    currentRow.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightPink);
                }

                // Hücre kenarlıkları
                currentRow.Style.Border.Top.Style = currentRow.Style.Border.Bottom.Style =
                currentRow.Style.Border.Left.Style = currentRow.Style.Border.Right.Style = ExcelBorderStyle.Thin;

                row++;
            }

            // Sütun genişliklerini ayarla
            worksheet.Column(1).Width = 20;  // Tarih
            worksheet.Column(2).Width = 15;  // Ad
            worksheet.Column(3).Width = 15;  // Soyad
            worksheet.Column(4).Width = 15;  // Kart No
            worksheet.Column(5).Width = 15;  // Departman
            worksheet.Column(6).Width = 12;  // Giriş
            worksheet.Column(7).Width = 12;  // Çıkış
            worksheet.Column(8).Width = 10;  // NCS
            worksheet.Column(9).Width = 10;  // Eksik
            worksheet.Column(10).Width = 10; // Mesai
            worksheet.Column(11).Width = 15; // Bilgi

            return File(
                package.GetAsByteArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Hareketler_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            );
        }
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
    [HttpPost]
    public ActionResult YansitHareketler()
    {
        try
        {
            // Burada gerekli işlemleri yapın (örneğin, hareketleri raporlar için güncelleme)
            // Bu örnekte sadece başarı mesajı döndürüyoruz.
            return Json(new { success = true, message = "Hareketler başarıyla yansıtıldı." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Hata oluştu: " + ex.Message });
        }
    }
    public ActionResult FazlaMesai(DateTime? StartDate, DateTime? EndDate, int? SelectedPersonelId)
    {
        // VardiyaListesi'ni al
        var vardiyaListesi = GetVardiyaListesi();

        DateTime startDate = StartDate ?? DateTime.Now.Date;
        DateTime endDate = EndDate ?? DateTime.Now.Date;
        endDate = endDate.AddHours(23).AddMinutes(59).AddSeconds(59).AddMilliseconds(999);

        var model = new HareketViewModel
        {
            PersonelListesi = GetPersonelListesi(),
            HareketListesi = GetHareketListesi(),
            VardiyaListesi = vardiyaListesi,
            SelectedPersonelId = SelectedPersonelId,
            StartDate = startDate,
            EndDate = endDate
        };

        model.HareketListesi = model.HareketListesi
            .Where(h => h.Tarih >= startDate && h.Tarih <= endDate)
            .ToList();

        if (SelectedPersonelId.HasValue)
        {
            model.HareketListesi = model.HareketListesi
                .Where(h => h.kartno == SelectedPersonelId.Value.ToString())
                .ToList();
        }

        model.HareketListesi = model.HareketListesi
            .GroupBy(h => new { h.Tarih, h.kartno })
            .Select(g =>
            {
                var hareket = new Hareket
                {
                    Tarih = g.Key.Tarih,
                    kartno = g.Key.kartno,
                    personelAdi = g.First().personelAdi,
                    personelSoyadi = g.First().personelSoyadi,
                    GirişSaatleri = g.Where(h => h.IslemTipi == "01")
                                     .OrderBy(h => h.Saat)
                                     .Select(h => h.Saat)
                                     .ToList(),
                    ÇıkışSaatleri = g.Where(h => h.IslemTipi == "02")
                                     .OrderByDescending(h => h.Saat)
                                     .Select(h => h.Saat)
                                     .ToList(),
                    Bilgi = !g.Any() ? "DEVAMSIZ" :
                            g.Any(h => h.IslemTipi == "01") && g.Any(h => h.IslemTipi == "02") ? "OK" :
                            g.Any(h => h.IslemTipi == "01") ? "ÇIKIŞ YOK" :
                            "GİRİŞ YOK"
                };

                var personel = model.PersonelListesi.FirstOrDefault(p => p.kartno == hareket.kartno);
                if (personel != null)
                {
                    hareket.PersonelId = personel.id;
                    hareket.personelAdi = personel.adi;
                    hareket.personelSoyadi = personel.soyadi;
                }

                return hareket;
            })
            .ToList();

        ViewBag.StartDate = startDate.ToString("yyyy-MM-dd");
        ViewBag.EndDate = endDate.ToString("yyyy-MM-dd");

        return View(model);
    }

    public ActionResult ExportIcmalExcel(DateTime? StartDate, DateTime? EndDate, int? SelectedPersonelId)
    {
        DateTime startDate = StartDate ?? DateTime.Now.Date;
        DateTime endDate = EndDate ?? DateTime.Now.Date;
        endDate = endDate.AddHours(23).AddMinutes(59).AddSeconds(59).AddMilliseconds(999);

        var personelListesi = GetPersonelListesi();
        var hareketListesi = GetHareketListesi();
        var vardiyaListesi = GetVardiyaListesi();

        if (SelectedPersonelId.HasValue)
        {
            var personel = personelListesi.FirstOrDefault(p => p.id == SelectedPersonelId.Value);
            if (personel != null)
            {
                hareketListesi = hareketListesi.Where(h => h.kartno == personel.kartno).ToList();
                personelListesi = personelListesi.Where(p => p.id == SelectedPersonelId.Value).ToList();
            }
        }

        var allDates = Enumerable.Range(0, (endDate - startDate).Days + 1)
                                 .Select(offset => startDate.AddDays(offset))
                                 .ToList();

        var hareketData = from personel in personelListesi
                          from tarih in allDates
                          let hareketler = hareketListesi.Where(h => h.Tarih.Date == tarih.Date && h.kartno == personel.kartno).ToList()
                          where hareketler.Any(h => h.IslemTipi == "01") && hareketler.Any(h => h.IslemTipi == "02") // Sadece mesai yaptığı günler
                          select new
                          {
                              Tarih = tarih,
                              personelAdi = personel.adi,
                              personelSoyadi = personel.soyadi,
                              kartno = personel.kartno,
                              GirişSaatleri = hareketler.Where(h => h.IslemTipi == "01").OrderBy(h => h.Saat).Select(h => h.Saat.ToString(@"hh\:mm")).ToList(),
                              ÇıkışSaatleri = hareketler.Where(h => h.IslemTipi == "02").OrderByDescending(h => h.Saat).Select(h => h.Saat.ToString(@"hh\:mm")).ToList()
                          };

        using (var package = new ExcelPackage())
        {
            var worksheet = package.Workbook.Worksheets.Add("FazlaMesaiRaporu");
            worksheet.Cells[1, 1].Value = "Tarih";
            worksheet.Cells[1, 2].Value = "Adı";
            worksheet.Cells[1, 3].Value = "Soyadı";
            worksheet.Cells[1, 4].Value = "Kart No";
            worksheet.Cells[1, 5].Value = "Giriş Saatleri";
            worksheet.Cells[1, 6].Value = "Çıkış Saatleri";
            worksheet.Cells[1, 7].Value = "Fazla Mesai";

            int row = 2;
            foreach (var data in hareketData)
            {
                worksheet.Cells[row, 1].Value = data.Tarih.ToString("dd.MM.yyyy ddd");
                worksheet.Cells[row, 2].Value = data.personelAdi ?? "Bilinmiyor";
                worksheet.Cells[row, 3].Value = data.personelSoyadi ?? "Bilinmiyor";
                worksheet.Cells[row, 4].Value = data.kartno;
                worksheet.Cells[row, 5].Value = data.GirişSaatleri.Any() ? string.Join(", ", data.GirişSaatleri) : "Giriş Yok";
                worksheet.Cells[row, 6].Value = data.ÇıkışSaatleri.Any() ? string.Join(", ", data.ÇıkışSaatleri) : "Çıkış Yok";

                string fazlaMesaiText = "0:00";
                if (data.GirişSaatleri.Any() && data.ÇıkışSaatleri.Any())
                {
                    var girisSaat = DateTime.ParseExact(data.GirişSaatleri.First(), "HH:mm", null);
                    var cikisSaat = DateTime.ParseExact(data.ÇıkışSaatleri.First(), "HH:mm", null);
                    var calismaSuresiTimeSpan = cikisSaat - girisSaat;

                    if (calismaSuresiTimeSpan.TotalHours > 9.30)
                    {
                        var fazlaMesai = calismaSuresiTimeSpan - TimeSpan.FromHours(9.30);
                        fazlaMesaiText = fazlaMesai.ToString(@"hh\:mm");
                    }
                }

                worksheet.Cells[row, 7].Value = fazlaMesaiText;

                row++;
            }

            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

            var stream = new MemoryStream(package.GetAsByteArray());
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "FazlaMesaiRaporu.xlsx");
        }
    }

    public ActionResult ExportToPdf2(DateTime? StartDate, DateTime? EndDate, int? SelectedPersonelId)
    {
        DateTime startDate = StartDate ?? DateTime.Now.Date;
        DateTime endDate = EndDate ?? DateTime.Now.Date;
        endDate = endDate.AddHours(23).AddMinutes(59).AddSeconds(59).AddMilliseconds(999);

        var personelListesi = GetPersonelListesi();
        var hareketListesi = GetHareketListesi();
        var vardiyaListesi = GetVardiyaListesi();

        if (SelectedPersonelId.HasValue)
        {
            var personel = personelListesi.FirstOrDefault(p => p.id == SelectedPersonelId.Value);
            if (personel != null)
            {
                hareketListesi = hareketListesi.Where(h => h.kartno == personel.kartno).ToList();
                personelListesi = personelListesi.Where(p => p.id == SelectedPersonelId.Value).ToList();
            }
        }

        var allDates = Enumerable.Range(0, (endDate - startDate).Days + 1)
                                 .Select(offset => startDate.AddDays(offset))
                                 .ToList();

        var hareketData = from tarih in allDates
                          from personel in personelListesi
                          let hareketler = hareketListesi.Where(h => h.Tarih.Date == tarih.Date && h.kartno == personel.kartno).ToList()
                          select new
                          {
                              Tarih = tarih,
                              PersonelId = personel.id,
                              personelAdi = personel.adi,
                              personelSoyadi = personel.soyadi,
                              kartno = personel.kartno,
                              GirişSaatleri = hareketler.Where(h => h.IslemTipi == "01").OrderBy(h => h.Saat).Select(h => h.Saat.ToString(@"hh\:mm")).ToList(),
                              ÇıkışSaatleri = hareketler.Where(h => h.IslemTipi == "02").OrderByDescending(h => h.Saat).Select(h => h.Saat.ToString(@"hh\:mm")).ToList(),
                              Bilgi = !hareketler.Any() ? "DEVAMSIZ" :
                                      hareketler.Any(h => h.IslemTipi == "01") && hareketler.Any(h => h.IslemTipi == "02") ? "OK" :
                                      hareketler.Any(h => h.IslemTipi == "01") ? "ÇIKIŞ YOK" :
                                      "GİRİŞ YOK",
                              Vardiya = vardiyaListesi.FirstOrDefault(v => v.VardiyaId == personel.VardiyaId)
                          };

        List<TimeSpan> calismaSuresiListesi = new List<TimeSpan>();
        List<TimeSpan> fazlaMesaiListesi = new List<TimeSpan>();
        List<TimeSpan> eksikCalismaListesi = new List<TimeSpan>();
        int toplamDevamsizlikGunSayisi = 0;

        using (var stream = new MemoryStream())
        {
            var pdfDoc = new PdfSharp.Pdf.PdfDocument();
            var page = pdfDoc.AddPage();
            var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Verdana", 10);
            var boldFont = new XFont("Verdana", 10);
            var headerBrush = new XSolidBrush(XColors.LightGray);
            var borderPen = new XPen(XColors.Black);

            double margin = 20;
            double yPoint = 40;
            double tableWidth = page.Width - 2 * margin;

            // Başlık
            gfx.DrawString("Hareket Değerlendirme", boldFont, XBrushes.Black, new XRect(margin, yPoint, tableWidth, page.Height), XStringFormats.TopLeft);
            yPoint += 30;

            // Tarih aralığı
            gfx.DrawString($"{startDate:dd-MM-yyyy} - {endDate:dd-MM-yyyy}", font, XBrushes.Black, new XRect(margin, yPoint, tableWidth, page.Height), XStringFormats.TopLeft);
            yPoint += 40;

            double[] columnWidths = { 0.2 * tableWidth, 0.1 * tableWidth, 0.1 * tableWidth, 0.15 * tableWidth, 0.15 * tableWidth, 0.1 * tableWidth, 0.1 * tableWidth, 0.1 * tableWidth };
            string[] headers = { "Tarih", "Ad", "Soyad", "Giriş Saatleri", "Çıkış Saatleri", "Çalışma Süresi", "Eksik Çalışma", "Fazla Mesai" };

            // Başlık satırı
            for (int i = 0; i < headers.Length; i++)
            {
                double xPoint = margin + columnWidths.Take(i).Sum();
                gfx.DrawRectangle(headerBrush, new XRect(xPoint, yPoint, columnWidths[i], 20));
                gfx.DrawRectangle(borderPen, new XRect(xPoint, yPoint, columnWidths[i], 20));
                gfx.DrawString(headers[i], boldFont, XBrushes.Black, new XRect(xPoint, yPoint, columnWidths[i], 20), XStringFormats.Center);
            }

            yPoint += 20;

            // Veri satırları
            foreach (var data in hareketData)
            {
                double xPoint = margin;

                gfx.DrawRectangle(borderPen, new XRect(xPoint, yPoint, columnWidths[0], 20));
                gfx.DrawString(data.Tarih.ToString("dd-MM-yyyy ddd"), font, XBrushes.Black, new XRect(xPoint, yPoint, columnWidths[0], 20), XStringFormats.TopLeft);
                xPoint += columnWidths[0];

                gfx.DrawRectangle(borderPen, new XRect(xPoint, yPoint, columnWidths[1], 20));
                gfx.DrawString(data.personelAdi ?? "Bilinmiyor", font, XBrushes.Black, new XRect(xPoint, yPoint, columnWidths[1], 20), XStringFormats.Center);
                xPoint += columnWidths[1];

                gfx.DrawRectangle(borderPen, new XRect(xPoint, yPoint, columnWidths[2], 20));
                gfx.DrawString(data.personelSoyadi ?? "Bilinmiyor", font, XBrushes.Black, new XRect(xPoint, yPoint, columnWidths[2], 20), XStringFormats.Center);
                xPoint += columnWidths[2];

                string girisSaatleri = data.GirişSaatleri.Any()
                    ? string.Join(", ", data.GirişSaatleri)
                    : "Giriş Yok";

                gfx.DrawRectangle(borderPen, new XRect(xPoint, yPoint, columnWidths[3], 20));
                gfx.DrawString(girisSaatleri, font, XBrushes.Black, new XRect(xPoint, yPoint, columnWidths[3], 20), XStringFormats.Center);
                xPoint += columnWidths[3];

                string cikisSaatleri = data.ÇıkışSaatleri.Any()
                    ? string.Join(", ", data.ÇıkışSaatleri)
                    : "Çıkış Yok";

                gfx.DrawRectangle(borderPen, new XRect(xPoint, yPoint, columnWidths[4], 20));
                gfx.DrawString(cikisSaatleri, font, XBrushes.Black, new XRect(xPoint, yPoint, columnWidths[4], 20), XStringFormats.Center);
                xPoint += columnWidths[4];

                string calismaSuresi = "Veri Eksik";
                string eksikSureText = "00:00";
                string fazlaMesaiText = "00:00";

                if (data.Bilgi == "DEVAMSIZ")
                {
                    calismaSuresi = "Devamsız";
                    toplamDevamsizlikGunSayisi++;
                }
                else if (data.GirişSaatleri.Any() && data.ÇıkışSaatleri.Any())
                {
                    var girisSaat = DateTime.ParseExact(data.GirişSaatleri.First(), "HH:mm", null);
                    var cikisSaat = DateTime.ParseExact(data.ÇıkışSaatleri.First(), "HH:mm", null);
                    var calismaSuresiTimeSpan = cikisSaat - girisSaat;

                    if (calismaSuresiTimeSpan < TimeSpan.Zero)
                    {
                        // Eğer çıkış saati giriş saatinden önceyse, bir sonraki güne kadar çalışılmış demektir
                        calismaSuresiTimeSpan += TimeSpan.FromDays(1);
                    }

                    calismaSuresi = calismaSuresiTimeSpan.ToString(@"hh\:mm");
                    calismaSuresiListesi.Add(calismaSuresiTimeSpan);

                    double calismaSaat = calismaSuresiTimeSpan.TotalHours;
                    var vardiya = data.Vardiya;
                    double vardiyaSaat = 9.30; // Varsayılan vardiya süresi 9.30 saat
                    if (vardiya != null)
                    {
                        vardiyaSaat = (vardiya.CalismaBitis - vardiya.CalismaBaslangic).TotalHours;
                    }

                    if (calismaSaat > vardiyaSaat)
                    {
                        double fazlaMesaiSaat = calismaSaat - vardiyaSaat;
                        fazlaMesaiListesi.Add(TimeSpan.FromHours(fazlaMesaiSaat));
                        fazlaMesaiText = $"{TimeSpan.FromHours(fazlaMesaiSaat):hh\\:mm}";
                    }

                    // Eksik çalışma süresi hesapla
                    if (calismaSaat < vardiyaSaat)
                    {
                        double eksikCalismaSaat = vardiyaSaat - calismaSaat;
                        eksikCalismaListesi.Add(TimeSpan.FromHours(eksikCalismaSaat));
                        eksikSureText = $"{TimeSpan.FromHours(eksikCalismaSaat):hh\\:mm}";
                    }
                }

                gfx.DrawRectangle(borderPen, new XRect(xPoint, yPoint, columnWidths[5], 20));
                gfx.DrawString(calismaSuresi, font, XBrushes.Black, new XRect(xPoint, yPoint, columnWidths[5], 20), XStringFormats.Center);
                xPoint += columnWidths[5];

                gfx.DrawRectangle(borderPen, new XRect(xPoint, yPoint, columnWidths[6], 20));
                gfx.DrawString(eksikSureText, font, XBrushes.Black, new XRect(xPoint, yPoint, columnWidths[6], 20), XStringFormats.Center);
                xPoint += columnWidths[6];

                gfx.DrawRectangle(borderPen, new XRect(xPoint, yPoint, columnWidths[7], 20));
                gfx.DrawString(fazlaMesaiText, font, XBrushes.Black, new XRect(xPoint, yPoint, columnWidths[7], 20), XStringFormats.Center);

                yPoint += 20;
            }

            // Toplam süreleri hesapla
            TimeSpan toplamCalismaSuresi = new TimeSpan(calismaSuresiListesi.Sum(ts => ts.Ticks));
            TimeSpan toplamFazlaMesaiSuresi = new TimeSpan(fazlaMesaiListesi.Sum(ts => ts.Ticks));
            TimeSpan toplamEksikCalismaSuresi = new TimeSpan(eksikCalismaListesi.Sum(ts => ts.Ticks));

            // Toplam süreleri hh:mm formatında yazdır
            yPoint += 40;
            gfx.DrawString($"Toplam Çalışma Süresi: {Math.Floor(toplamCalismaSuresi.TotalHours):00}:{toplamCalismaSuresi.Minutes:00}", font, XBrushes.Black, new XRect(margin, yPoint, tableWidth, page.Height), XStringFormats.TopLeft);
            yPoint += 20;
            gfx.DrawString($"Toplam Fazla Mesai: {Math.Floor(toplamFazlaMesaiSuresi.TotalHours):00}:{toplamFazlaMesaiSuresi.Minutes:00}", font, XBrushes.Black, new XRect(margin, yPoint, tableWidth, page.Height), XStringFormats.TopLeft);
            yPoint += 20;
            gfx.DrawString($"Toplam Eksik Çalışma: {Math.Floor(toplamEksikCalismaSuresi.TotalHours):00}:{toplamEksikCalismaSuresi.Minutes:00}", font, XBrushes.Black, new XRect(margin, yPoint, tableWidth, page.Height), XStringFormats.TopLeft);
            yPoint += 20;
            gfx.DrawString($"Toplam Devamsızlık: {toplamDevamsizlikGunSayisi} gün", font, XBrushes.Black, new XRect(margin, yPoint, tableWidth, page.Height), XStringFormats.TopLeft);

            pdfDoc.Save(stream, false);

            return File(stream.ToArray(), "application/pdf", "PersonelIcmalRaporu.pdf");
        }
    }
    public ActionResult IcmalRaporu(DateTime? StartDate, DateTime? EndDate, int? SelectedPersonelId)
    {
        // Varsayılan tarih aralığını alalım
        DateTime startDate = StartDate ?? DateTime.Now.Date;
        DateTime endDate = EndDate ?? DateTime.Now.Date;

        endDate = endDate.AddHours(23).AddMinutes(59).AddSeconds(59).AddMilliseconds(999);

        var model = new HareketViewModel
        {
            PersonelListesi = GetPersonelListesi(),
            HareketListesi = GetHareketListesi(),
            SelectedPersonelId = SelectedPersonelId,
            StartDate = startDate,
            EndDate = endDate
        };

        // Hareketleri tarih aralığına göre filtreleyelim
        model.HareketListesi = model.HareketListesi
            .Where(h => h.Tarih >= startDate && h.Tarih <= endDate)
            .ToList();

        // Eğer SelectedPersonelId seçilmişse, sadece o kişiye ait hareketleri filtrele
        if (SelectedPersonelId.HasValue)
        {
            var personel = model.PersonelListesi.FirstOrDefault(p => p.id == SelectedPersonelId.Value);
            if (personel != null)
            {
                model.HareketListesi = model.HareketListesi
                    .Where(h => h.kartno == personel.kartno)
                    .ToList();
            }
        }

        // Personel bilgilerini hareketlere ekle
        foreach (var hareket in model.HareketListesi)
        {
            var personel = model.PersonelListesi.FirstOrDefault(p => p.kartno == hareket.kartno);
            if (personel != null)
            {
                hareket.personelAdi = $"{personel.adi}";
                hareket.personelSoyadi = $"{personel.soyadi}";
            }
        }

        // Raporu görüntüle
        return View(model);
    }
    private List<DateTime> GetDaysList(DateTime startDate, DateTime endDate)
    {
        var daysList = new List<DateTime>();

        // En fazla 30 gün olacak şekilde liste oluştur
        while (startDate <= endDate && daysList.Count < 30)
        {
            daysList.Add(startDate);
            startDate = startDate.AddDays(1); // Bir sonraki güne geç
        }

        return daysList;
    }
    [HttpPost]
    public List<HareketRaporu> GetHareketRaporu()
    {
        using (var context = new YillikizinEntities()) // Veritabanı bağlamı
        {
            var hareketListesi = context.Hareketler
                .Where(h => h.Yon == "01" || h.Yon == "02") // Sadece giriş (001) ve çıkış (002) işlemleri
                .OrderBy(h => h.Tarih)
                .ThenBy(h => h.Saat)
                .ToList();

            var raporListesi = new List<HareketRaporu>();

            // Hareket verilerini rapora dönüştürme
            foreach (var hareket in hareketListesi)
            {
                var rapor = raporListesi.FirstOrDefault(r => r.Tarih.Date == hareket.Tarih && r.PersonelAdi == (hareket.personel != null ? hareket.personel.adi : "Bilinmiyor") && r.PersonelSoyadi == (hareket.personel != null ? hareket.personel.soyadi : "Bilinmiyor"));

                if (rapor == null)
                {
                    rapor = new HareketRaporu
                    {
                        Tarih = hareket.Tarih ?? DateTime.MinValue,
                        PersonelAdi = hareket.personel != null ? hareket.personel.adi : "Bilinmiyor",
                        PersonelSoyadi = hareket.personel != null ? hareket.personel.soyadi : "Bilinmiyor",
                        kartno = hareket.KartNumarasi,
                        GirişSaatleri = new List<TimeSpan>(),
                        ÇıkışSaatleri = new List<TimeSpan>()
                    };
                    raporListesi.Add(rapor);
                }

                // Giriş saati ekle (sadece saat ve dakika)
                if (hareket.Yon == "01")
                {
                    var girisSaati = hareket.Saat ?? TimeSpan.Zero;
                    rapor.GirişSaatleri.Add(new TimeSpan(girisSaati.Hours, girisSaati.Minutes, 0)); // Sadece saat ve dakika
                }

                // Çıkış saati ekle (sadece saat ve dakika)
                if (hareket.Yon == "02")
                {
                    var cikisSaati = hareket.Saat ?? TimeSpan.Zero;
                    rapor.ÇıkışSaatleri.Add(new TimeSpan(cikisSaati.Hours, cikisSaati.Minutes, 0)); // Sadece saat ve dakika
                }

            }

            return raporListesi;
        }
    }
    // Silme işlemi için POST metodu
    [HttpPost]
    public ActionResult DeleteHareket(int id)
    {
        try
        {
            using (var db = new YillikizinEntities()) // Veritabanı işlemi için using bloğu
            {
                // Silinecek hareketi bul
                var hareket = db.Hareketler.FirstOrDefault(h => h.Id == id);

                if (hareket != null)
                {
                    // Hareketi sil
                    db.Hareketler.Remove(hareket);
                    db.SaveChanges();
                    return Json(new { success = true, message = "Hareket başarıyla silindi." });
                }
                else
                {
                    return Json(new { success = false, message = "Hareket bulunamadı." });
                }
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Bir hata oluştu: " + ex.Message });
        }
    }

    private List<Hareket> GetHareketListesiRapor()
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
                    kartno = h.KartNumarasi,

                })
                .OrderBy(h => h.Tarih) // Tarihe göre sıralama
                .ThenBy(h => h.Saat)  // Saate göre sıralama
                .ToList();

            return hareketListesi;
        }
    }
    // Hareket Listesi Metodu
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
                    Bilgi = h.Bilgi,
                    kartno = h.KartNumarasi

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
    private List<personel> GetPersonelListesi()
    {
        using (var db = new YillikizinEntities())
        {
            return db.personel.ToList();
        }
    }


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
        using (var db = new YillikizinEntities())
        {
            var hareketListesi = db.Hareketler
                .Where(h => h.KartNumarasi == personel.kartno) // kartno karşılaştırması
                .OrderBy(h => h.Tarih)
                .ThenBy(h => h.Saat)
                .ToList();

            // HareketListesi'ni Hareket türüne dönüştürme
            List<Hareket> hareketler = hareketListesi.Select(h => new Hareket
            {
                id = h.Id,
                kartno = h.KartNumarasi,
                // Tarih nullable DateTime olduğu için, null kontrolü ekledik
                Tarih = h.Tarih.HasValue ? h.Tarih.Value : DateTime.MinValue, // Null ise MinValue kullan
                TerminalNo = h.TerminalNo,
                // Saat nullable TimeSpan olduğu için, null kontrolü ekledik
                Saat = h.Saat.HasValue ? h.Saat.Value : TimeSpan.Zero, // Null ise Zero kullan
                IslemTipi = h.Yon == "01" ? "Giriş" : "Çıkış"
            }).ToList();

            return hareketler; // Son listeyi döndür
        }
    }

    private personel GetPersonelById(int personelId)
    {
        using (var db = new YillikizinEntities())
        {
            return db.personel.FirstOrDefault(p => p.id == personelId);
        }
    }

    private List<Izin> GetIzinListesi()
    {
        using (var db = new YillikizinEntities())
        {
            return db.Izin.ToList();
        }
    }
    [HttpPost]
    public ActionResult TopluIslem(string islemTuru, DateTime baslangicTarihi, DateTime bitisTarihi, string saat, string yon, string kartNo)
    {
        try
        {
            using (var db = new YillikizinEntities())
            {
                var startDate = baslangicTarihi.Date;
                var endDate = bitisTarihi.Date;
                TimeSpan? hareketZamani = null;

                if (islemTuru == "ekle" && !string.IsNullOrWhiteSpace(saat))
                {
                    hareketZamani = TimeSpan.Parse(saat);
                }

                // Kart numarasına göre personeli bul
                var personel = db.personel.FirstOrDefault(p => p.kartno == kartNo);
                if (personel == null)
                {
                    return Json(new { success = false, message = "Personel bulunamadı." });
                }

                if (islemTuru == "ekle" && hareketZamani.HasValue)
                {
                    for (var date = startDate; date <= endDate; date = date.AddDays(1))
                    {
                        var yeniHareket = new Hareketler
                        {
                            KartNumarasi = kartNo,
                            TerminalNo = "01",
                            Tarih = date,
                            Saat = hareketZamani.Value,
                            Yon = yon
                        };
                        db.Hareketler.Add(yeniHareket);
                    }
                }
                else if (islemTuru == "sil")
                {
                    IQueryable<Hareketler> hareketler;

                    if (yon == "03") // Belirsiz Yön
                    {
                        hareketler = db.Hareketler.Where(h => h.KartNumarasi == kartNo
                            && h.Tarih >= startDate && h.Tarih <= endDate);
                    }
                    else
                    {
                        hareketler = db.Hareketler.Where(h => h.KartNumarasi == kartNo
                            && h.Tarih >= startDate && h.Tarih <= endDate && h.Yon == yon);
                    }

                    db.Hareketler.RemoveRange(hareketler);
                }

                db.SaveChanges();
                return Json(new { success = true });
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Hata oluştu: " + ex.Message });
        }
    }    // Controller - YeniHareketEkle
    [HttpPost]

    public ActionResult YeniHareketEkle(int personelId, string terminalNo, string tarih, string saat, string yon)
    {
        try
        {
            // Parametre kontrolü
            if (personelId <= 0 || string.IsNullOrEmpty(tarih) || string.IsNullOrEmpty(saat) || string.IsNullOrEmpty(yon))
            {
                return Json(new { success = false, message = "Lütfen tüm alanları doldurunuz." });
            }

            // Tarih ve saat formatını kontrol et ve birleştir
            if (!DateTime.TryParseExact(
                $"{tarih} {saat}",
                "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime hareketTarihi))
            {
                return Json(new { success = false, message = "Geçersiz tarih veya saat formatı." });
            }

            using (var db = new YillikizinEntities())
            {
                // Personeli bul
                var personel = db.personel.FirstOrDefault(p => p.id == personelId);
                if (personel == null)
                {
                    return Json(new { success = false, message = "Personel bulunamadı." });
                }

                // Aynı personel için aynı zamanda hareket kontrolü
                var ayniZamandaHareket = db.Hareketler.Any(h =>
                    h.KartNumarasi == personel.kartno &&
                    h.Tarih == hareketTarihi.Date &&
                    h.Saat == hareketTarihi.TimeOfDay);

                if (ayniZamandaHareket)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Bu personel için seçilen tarih ve saatte zaten bir hareket kaydı mevcut."
                    });
                }

                // Yeni hareket kaydı oluştur
                var yeniHareket = new Hareketler
                {
                    KartNumarasi = personel.kartno, // Personelden otomatik al
                    TerminalNo = terminalNo ?? "001",  // Varsayılan terminal numarası
                    Tarih = hareketTarihi.Date,
                    Saat = hareketTarihi.TimeOfDay,
                    Yon = yon
                };

                db.Hareketler.Add(yeniHareket);
                db.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "Hareket başarıyla kaydedildi.",
                    personelAdi = personel.adi + " " + personel.soyadi
                });
            }
        }
        catch (Exception ex)
        {
            // Hata loglanabilir
            return Json(new
            {
                success = false,
                message = "Hareket kaydedilirken bir hata oluştu: " + ex.Message
            });
        }
    }
    [HttpPost]
    public ActionResult UpdateHareket(int id, string kartNo, string tarih, string saat, string yon, bool yonDegistir)
    {
        try
        {
            using (var db = new YillikizinEntities())
            {
                var hareket = db.Hareketler.FirstOrDefault(h => h.Id == id);

                if (hareket != null)
                {
                    // Tarih güncelleme
                    if (!string.IsNullOrEmpty(tarih))
                    {
                        hareket.Tarih = DateTime.ParseExact(tarih, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    }

                    // Saat güncelleme - Sadece yeni değer girilmişse
                    if (!string.IsNullOrEmpty(saat))
                    {
                        // Mevcut TimeSpan değerini koru, yeni bir değer girilmişse güncelle
                        try
                        {
                            var saatParcalari = saat.Split(':');
                            if (saatParcalari.Length >= 2)
                            {
                                int saat_ = Convert.ToInt32(saatParcalari[0]);
                                int dakika = Convert.ToInt32(saatParcalari[1]);
                                hareket.Saat = new TimeSpan(saat_, dakika, 0);
                            }
                        }
                        catch
                        {
                            // Hata durumunda mevcut saati değiştirme
                        }
                    }

                    // Yön güncelleme
                    if (yon == "01" || yon == "02")
                    {
                        hareket.Yon = yon;
                    }

                    if (yonDegistir)
                    {
                        var kartNumarasi = hareket.KartNumarasi;
                        var secilenTarih = hareket.Tarih.Value;
                        var secilenSaat = hareket.Saat ?? DateTime.Now.TimeOfDay;

                        var sonrakiHareketler = db.Hareketler
                            .Where(h => h.KartNumarasi == kartNumarasi &&
                                      ((h.Tarih > secilenTarih) ||
                                       (h.Tarih == secilenTarih && h.Saat > secilenSaat)))
                            .OrderBy(h => h.Tarih)
                            .ThenBy(h => h.Saat)
                            .ToList();

                        foreach (var sonrakiHareket in sonrakiHareketler)
                        {
                            sonrakiHareket.Yon = sonrakiHareket.Yon == "01" ? "02" : "01";
                        }
                    }

                    db.SaveChanges();

                    return Json(new
                    {
                        success = true,
                        id = hareket.Id,
                        kartNo = hareket.KartNumarasi,
                        tarih = hareket.Tarih?.ToString("yyyy-MM-dd"),
                        saat = hareket.Saat?.ToString(@"HH\:mm"),
                        yon = hareket.Yon,
                        terminalNo = hareket.TerminalNo
                    });
                }
                else
                {
                    return Json(new { success = false, message = "Hareket bulunamadı." });
                }
            }
        }
        catch (Exception ex)
        {
            return View(Index);
        }
    }
}