using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using Quartz;
using yillikizin.Models;
using Quartz.Impl;
using System.Data.Entity;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;

namespace yillikizin.Controllers
{
    [OzelYetki]
    public class EmailController : Controller
    {
        private YillikizinEntities db = new YillikizinEntities();
        public ActionResult Index()
        {
            return View();
        }

        public ActionResult Eposta()
        {
            // Veritabanından mevcut e-posta ayarlarını al
            var emailSettings = new EmailSettings
            {
                EmailSettingsList = db.EmailSettings.ToList()
            };

            return View(emailSettings);
        }
        [HttpPost]
        public async Task<ActionResult> Eposta(EmailSettings model, string[] RecipientEmails)
        {
            if (ModelState.IsValid)
            {
                foreach (var email in RecipientEmails)
                {
                    // Yeni EmailSettings nesnesi oluştur ve e-posta adresini ata
                    var newEmailSettings = new EmailSettings
                    {
                        RecipientEmails = email.Trim(),
                        SendTime = model.SendTime
                    };

                    // Veritabanına ekle
                    db.EmailSettings.Add(newEmailSettings);
                }

                // Değişiklikleri kaydet
                await db.SaveChangesAsync();

                // Quartz job ayarlarını güncelle
                await ScheduleEmailJob();

                return RedirectToAction("Eposta");
            }

            model.EmailSettingsList = db.EmailSettings.ToList();
            return View(model);
        }

        private async Task ScheduleEmailJob()
        {
            // Quartz.NET Scheduler'ı başlat
            StdSchedulerFactory factory = new StdSchedulerFactory();
            IScheduler scheduler = await factory.GetScheduler();
            await scheduler.Start();
            //Application["Scheduler"] = scheduler;

            Console.WriteLine("Scheduler başlatıldı ve çalışıyor."); // Scheduler'ın başlatıldığını logla

            var settings = await db.EmailSettings.ToListAsync();
            if (settings != null && settings.Last().SendTime.HasValue)
            {
                var sendTime = settings.Last().SendTime.Value;
                // Cron formatı: "saniye dakika saat gün ay haftanın günü"
                string cronExpression = $"0 {sendTime.Minutes} {sendTime.Hours} * * ?";

                // Mevcut trigger'ı kontrol et ve varsa sil
                var existingTrigger = await scheduler.GetTrigger(new TriggerKey("dailyTrigger", "emailGroup"));
                if (existingTrigger != null)
                {
                    await scheduler.UnscheduleJob(new TriggerKey("dailyTrigger", "emailGroup"));
                }

                ITrigger trigger = TriggerBuilder.Create()
                    .WithIdentity("dailyTrigger", "emailGroup")
                    .WithCronSchedule(cronExpression)
                    .StartNow()
                    .Build();

                // Job ve trigger'ı planla
                //IScheduler scheduler = (IScheduler)HttpContext.Application["Scheduler"];
                //await scheduler.Start();

                IJobDetail job = JobBuilder.Create<EmailJob>()
                    .WithIdentity("dailyJob", "emailGroup")
                    .Build();

                var result = await scheduler.ScheduleJob(job, trigger);

                Console.WriteLine("Job ve Trigger doğru şekilde zamanlandı."); // Job ve Trigger'ın zamanlandığını logla
            }
        }
        [HttpPost]
        public async Task<ActionResult> EditEmailSetting(EmailSettings model)
        {
            if (ModelState.IsValid)
            {
                var emailSetting = await db.EmailSettings.FindAsync(model.Id);
                if (emailSetting != null)
                {
                    emailSetting.RecipientEmails = model.RecipientEmails;
                    emailSetting.SendTime = model.SendTime;
                    await db.SaveChangesAsync();
                }
                return RedirectToAction("Eposta");
            }
            return View(model);
        }

        [HttpPost]
        public async Task<ActionResult> DeleteEmailSetting(int id)
        {
            var emailSetting = await db.EmailSettings.FindAsync(id);
            if (emailSetting != null)
            {
                db.EmailSettings.Remove(emailSetting);
                await db.SaveChangesAsync();
            }
            return RedirectToAction("Eposta");
        }
        // PDF olarak oluşturulacak rapor
        [HttpPost]
        public async Task<ActionResult> SendEmail()
        {
            try
            {
                var emailSettingsList = await db.EmailSettings.ToListAsync();

                if (!emailSettingsList.Any())
                {
                    TempData["Error"] = "E-posta gönderilecek adres bulunamadı.";
                    return RedirectToAction("Index");
                }

                // Sadece bugünün tarihini kullan
                DateTime today = DateTime.Now.Date;

                // Excel dosyasını oluştur
                byte[] excelBytes;
                using (var package = CreateExcelPackage(today, today, null))
                {
                    excelBytes = package.GetAsByteArray();
                }

                // Excel dosyasını memory stream'e aktar
                using (var ms = new MemoryStream(excelBytes))
                {
                    using (var emailService = new EmailService())
                    {
                        foreach (var emailSetting in emailSettingsList)
                        {
                            if (!string.IsNullOrEmpty(emailSetting.RecipientEmails))
                            {
                                var recipientEmails = emailSetting.RecipientEmails
                                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(e => e.Trim())
                                    .Where(e => !string.IsNullOrEmpty(e));

                                foreach (var email in recipientEmails)
                                {
                                    try
                                    {
                                        ms.Position = 0;
                                        await emailService.SendEmailWithAttachmentAsync(
                                            email,
                                            "Günlük Hareket Raporu",
                                            $"{today:dd.MM.yyyy} tarihli hareket raporu ekte bulunmaktadır.",
                                            ms,
                                            $"Hareket_Raporu_{today:yyyyMMdd}.xlsx"
                                        );
                                        Console.WriteLine($"E-posta gönderildi: {email}");
                                    }
                                    catch (Exception emailEx)
                                    {
                                        Console.WriteLine($"E-posta gönderimi başarısız oldu ({email}): {emailEx.Message}");
                                    }
                                }
                            }
                        }
                    }
                }

                TempData["Success"] = $"{today:dd.MM.yyyy} tarihli raporlar başarıyla gönderildi.";
                return RedirectToAction("Eposta");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"İşlem sırasında hata oluştu: {ex.Message}";
                return RedirectToAction("Eposta");
            }
        }
        private ExcelPackage CreateExcelPackage(DateTime startDate, DateTime endDate, string filterType)
        {
            var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Hareketler");

            // Verileri al
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
                                   GirişDurumu = GetGirisDurumu(hareketler, vardiya),
                                   Vardiya = vardiya,
                                   IsPazar = tarih.DayOfWeek == DayOfWeek.Sunday,
                                   Izin = izin
                               };

            // Filtrele
            if (!string.IsNullOrEmpty(filterType))
            {
                filteredData = filterType switch
                {
                    "Erken" => filteredData.Where(d => d.GirişSaatleri.Any() && d.GirişDurumu == "Erken" &&
                                                      d.Bilgi != "DEVAMSIZ" && d.Bilgi != "HAFTA TATİLİ" && d.Izin == null),
                    "Geç" => filteredData.Where(d => d.GirişSaatleri.Any() && d.GirişDurumu == "Geç" &&
                                                    d.Bilgi != "DEVAMSIZ" && d.Bilgi != "HAFTA TATİLİ" && d.Izin == null),
                    "Izinliler" => filteredData.Where(d => d.Izin != null),
                    "Devamsiz" => filteredData.Where(d => d.Bilgi == "DEVAMSIZ"),
                    _ => filteredData
                };
            }

            // Excel başlıkları
            var headers = new[] { "Tarih", "Adı", "Soyadı", "Kart No", "Departman", "Giriş", "Çıkış",
                         "NCS", "Eksik", "Mesai", "Bilgi" };

            // Başlıkları ekle
            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cells[1, i + 1].Value = headers[i];
                worksheet.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                worksheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(Color.LightGray);
                worksheet.Cells[1, i + 1].Style.Font.Bold = true;
            }

            // Verileri ekle
            int row = 2;
            foreach (var data in filteredData)
            {
                worksheet.Cells[row, 1].Value = data.Tarih.ToString("yyyy-MM-dd ddd");
                worksheet.Cells[row, 2].Value = data.Adi;
                worksheet.Cells[row, 3].Value = data.Soyadi;
                worksheet.Cells[row, 4].Value = data.KartNo;
                worksheet.Cells[row, 5].Value = data.Departman;

                // Giriş saatleri
                var girisSaati = data.GirişSaatleri.FirstOrDefault();
                worksheet.Cells[row, 6].Value = girisSaati != TimeSpan.Zero ?
                    girisSaati.ToString(@"hh\:mm") : "00:00";

                // Çıkış saatleri
                var cikisSaati = data.ÇıkışSaatleri.FirstOrDefault();
                worksheet.Cells[row, 7].Value = cikisSaati != TimeSpan.Zero ?
                    cikisSaati.ToString(@"hh\:mm") : "00:00";

                // Çalışma süresi
                if (girisSaati != TimeSpan.Zero && cikisSaati != TimeSpan.Zero)
                {
                    var calismaSuresi = cikisSaati - girisSaati;
                    worksheet.Cells[row, 8].Value = calismaSuresi.ToString(@"hh\:mm");

                    if (data.Vardiya != null)
                    {
                        var vardiyaSuresi = data.Vardiya.CalismaBitis - data.Vardiya.CalismaBaslangic;

                        // Eksik süre
                        if (vardiyaSuresi > calismaSuresi)
                        {
                            worksheet.Cells[row, 9].Value = (vardiyaSuresi - calismaSuresi).ToString(@"hh\:mm");
                        }

                        // Fazla mesai
                        if (calismaSuresi > vardiyaSuresi)
                        {
                            worksheet.Cells[row, 10].Value = (calismaSuresi - vardiyaSuresi).ToString(@"hh\:mm");
                        }
                    }
                }

                // Bilgi
                worksheet.Cells[row, 11].Value = data.Bilgi;

                // Satır stilini ayarla
                var currentRow = worksheet.Cells[row, 1, row, 11];
                if (data.IsPazar)
                {
                    currentRow.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    currentRow.Style.Fill.BackgroundColor.SetColor(Color.LightYellow);
                }
                else if (data.Izin != null)
                {
                    currentRow.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    currentRow.Style.Fill.BackgroundColor.SetColor(Color.LightGreen);
                }
                else if (data.Bilgi == "DEVAMSIZ")
                {
                    currentRow.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    currentRow.Style.Fill.BackgroundColor.SetColor(Color.LightPink);
                }

                row++;
            }

            // Sütun genişliklerini ayarla
            for (int i = 1; i <= 11; i++)
            {
                worksheet.Column(i).AutoFit();
            }

            return package;
        }

        private string GetGirisDurumu(List<Hareket> hareketler, Vardiya vardiya)
        {
            if (!hareketler.Any(h => h.IslemTipi == "01") || vardiya == null)
                return "";

            var girisSaati = hareketler.Where(h => h.IslemTipi == "01")
                                      .OrderBy(h => h.Saat)
                                      .Select(h => h.Saat)
                                      .FirstOrDefault();

            if (girisSaati < vardiya.ErkenGelme)
                return "Erken";
            if (girisSaati > vardiya.GecGelme)
                return "Geç";
            return "OK";
        }
        private List<Izin> GetIzinListesi()
        {
            using (var db = new YillikizinEntities())
            {
                return db.Izin.ToList();
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
    }
}