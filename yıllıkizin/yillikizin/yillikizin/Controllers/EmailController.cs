using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using Quartz;
using Rotativa;
using yillikizin.Models;
using Quartz.Impl;
using yillikizin.Filters;
using System.Data.Entity;
using DocumentFormat.OpenXml.Drawing;

namespace yillikizin.Controllers
{
    [CustomAuthorize]
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
        [HttpPost]
        public async Task<ActionResult> SendTestEmail()
        {
            try
            {
                var settings = db.EmailSettings.FirstOrDefault();
                if (settings != null)
                {
                    var recipientEmails = settings.RecipientEmails.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    var emailService = new EmailService();
                    string subject = "Test E-postası";
                    string body = "Bu, test amaçlı gönderilen bir e-postadır.";

                    // PDF oluşturma işlemini burada gerçekleştiriyoruz
                    var pdfStream = GeneratePdfStream(DateTime.Now.AddDays(-1), DateTime.Now);

                    using (pdfStream)
                    {
                        foreach (var recipientEmail in recipientEmails)
                        {
                            pdfStream.Position = 0; // Stream'i başa döndür
                            await emailService.SendEmailWithAttachmentAsync(recipientEmail, subject, body, pdfStream, "TestRaporu.pdf");
                        }
                    }

                    ViewBag.Message = "Test e-postası başarıyla gönderildi.";
                }
                else
                {
                    ViewBag.Message = "E-posta ayarları bulunamadı.";
                }
            }
            catch (Exception ex)
            {
                ViewBag.Message = $"Test e-postası gönderimi başarısız oldu: {ex.Message}";
            }

            return RedirectToAction("Eposta");
        }

        // PDF olarak oluşturulacak rapor
        [AllowAnonymous]
        public ActionResult HareketDegerlendirmeReport(DateTime? StartDate, DateTime? EndDate)
        {
            DateTime startDate = StartDate ?? DateTime.Now.Date;
            DateTime endDate = EndDate ?? DateTime.Now.Date;
            endDate = endDate.AddHours(23).AddMinutes(59).AddSeconds(59).AddMilliseconds(999);

            var model = new HareketViewModel
            {
                PersonelListesi = GetPersonelListesi(),
                HareketListesi = GetHareketListesi(),
                StartDate = startDate,
                EndDate = endDate
            };

            // Tarih aralığına göre filtreleme
            model.HareketListesi = model.HareketListesi
                .Where(h => h.Tarih >= startDate && h.Tarih <= endDate)
                .ToList();

            // Gruplama ve hareket bilgilerini doldurma
            model.HareketListesi = model.HareketListesi
                .GroupBy(h => new { h.Tarih, h.PersonelId })
                .Select(g =>
                {
                    var hareket = new Hareket
                    {
                        Tarih = g.Key.Tarih,
                        PersonelId = g.Key.PersonelId,
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

                    // Vardiya bilgilerini al ve giriş durumunu belirle
                    var personel = model.PersonelListesi.FirstOrDefault(p => p.id == hareket.PersonelId);
                    if (personel != null)
                    {
                        var vardiya = GetVardiyaById(personel.VardiyaId);
                        if (vardiya != null)
                        {
                            TimeSpan erkenGelmeSaati = vardiya.ErkenGelme;
                            TimeSpan gecGelmeSaati = vardiya.GecGelme;
                            var girisSaati = hareket.GirişSaatleri.FirstOrDefault();
                            if (girisSaati != null && girisSaati.Hours > 0)
                            {
                                if (girisSaati < erkenGelmeSaati)
                                {
                                    hareket.GirişDurumu = "Erken";
                                }
                                else if (girisSaati > gecGelmeSaati)
                                {
                                    hareket.GirişDurumu = "Geç";
                                }
                                else
                                {
                                    hareket.GirişDurumu = "OK";
                                }
                            }
                        }
                    }

                    return hareket;
                })
                .ToList();

            return View(model);
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
        public async Task<ActionResult> SendEmail(DateTime? StartDate, DateTime? EndDate)
        {
            using (var db = new YillikizinEntities())
            {
                try
                {
                    var emailSettingsList = await db.EmailSettings.ToListAsync();

                    if (!emailSettingsList.Any())
                    {
                        TempData["Error"] = "E-posta gönderilecek adres bulunamadı.";
                        return RedirectToAction("Index");
                    }

                    StartDate = StartDate ?? DateTime.Now.AddDays(-1);
                    EndDate = EndDate ?? DateTime.Now;

                    using (var pdfStream = CreatePdfStream(StartDate, EndDate)) // Eski PDF metodunu kullan
                    {
                        Console.WriteLine("PDF oluşturma işlemi başarılı.");

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
                                            pdfStream.Position = 0; // Stream'i başa sar
                                            await emailService.SendEmailWithAttachmentAsync(
                                                email,
                                                "Rapor",
                                                "Rapor ekte bulunmaktadır.",
                                                pdfStream,
                                                $"Rapor_{DateTime.Now:yyyyMMdd}.pdf"
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

                    TempData["Success"] = "E-postalar başarıyla gönderildi.";
                    Console.WriteLine("Tüm e-posta gönderim işlemleri tamamlandı.");
                }
                catch (Exception ex)
                {
                    TempData["Error"] = $"İşlem sırasında hata oluştu: {ex.Message}";
                    Console.WriteLine($"Genel bir hata oluştu: {ex.Message}");
                }
            }

            return RedirectToAction("Eposta");
        }

        // Eski PDF oluşturma metodu
        public MemoryStream CreatePdfStream(DateTime? StartDate, DateTime? EndDate)
        {
            try
            {
                var pdfResult = new ActionAsPdf("HareketDegerlendirmeReport", new { StartDate, EndDate })
                {
                    FileName = "Rapor.pdf",
                    PageSize = Rotativa.Options.Size.A4,
                    PageOrientation = Rotativa.Options.Orientation.Portrait,
                    CustomSwitches = "--disable-smart-shrinking"
                };

                var pdfData = pdfResult.BuildFile(ControllerContext);
                if (pdfData == null || pdfData.Length == 0)
                {
                    throw new InvalidOperationException("PDF oluşturulamadı.");
                }

                var pdfStream = new MemoryStream(pdfData);
                pdfStream.Position = 0;
                Console.WriteLine($"PDF başarıyla oluşturuldu. Boyut: {pdfData.Length} bytes");
                return pdfStream;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PDF oluşturma hatası: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"İç hata: {ex.InnerException.Message}");
                }
                throw;
            }
        }
        private MemoryStream GeneratePdfStream(DateTime? StartDate, DateTime? EndDate)
        {
            try
            {
                if (!StartDate.HasValue || !EndDate.HasValue)
                {
                    throw new ArgumentException("Başlangıç ve bitiş tarihleri gereklidir.");
                }

                var pdfResult = new ActionAsPdf("HareketDegerlendirmeReport", new { StartDate, EndDate })
                {
                    FileName = "Rapor.pdf"
                };

                byte[] pdfData = pdfResult.BuildFile(ControllerContext);
                if (pdfData == null || pdfData.Length == 0)
                {
                    throw new InvalidOperationException("PDF oluşturulamadı veya boş.");
                }

                var pdfStream = new MemoryStream(pdfData);
                Console.WriteLine($"PDF başarıyla oluşturuldu. Boyut: {pdfData.Length} bytes");
                return pdfStream;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PDF oluşturma hatası: {ex.Message}");
                throw new Exception("PDF raporu oluşturulurken bir hata oluştu.", ex);
            }
        }
        // PDF oluşturma işlemi için ayrı bir metod
    }
}