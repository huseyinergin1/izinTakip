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
                using (var package = CreateExcelPackage(today, "filterType"))
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

        private ExcelPackage CreateExcelPackage(DateTime date, string filterType)
        {
            var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Hareketler");

            // Verileri al
            var personelListesi = GetPersonelListesi().Where(p => p.calisma == true).ToList();
            var hareketListesi = GetHareketListesi();
            var vardiyaListesi = GetVardiyaListesi();
            var izinListesi = GetIzinListesi();

            var aktifPersoneller = GetPersonelListesi().Where(p => p.calisma == true).ToList();
            // Sadece bugünün tarihini baz al
            DateTime startDate = date.Date;
            DateTime endDate = date.Date.AddHours(23).AddMinutes(59).AddSeconds(59).AddMilliseconds(999);

            // Tarih filtresi uygula
            hareketListesi = hareketListesi
                .Where(h => h.Tarih >= startDate && h.Tarih <= endDate)
                .ToList();

            // Sadece aktif personellerin hareketlerini filtrele
            hareketListesi = hareketListesi
                .Where(h => personelListesi.Any(p => p.kartno == h.kartno))
                .ToList();

            // HareketDegerlendirme'den alınan mantık ile hareketleri işle
            var processedHareketler = hareketListesi
                .GroupBy(h => new { h.Tarih.Date, h.kartno })
                .Select(g =>
                {
                    var hareket = new Hareket
                    {
                        Tarih = g.Key.Date,
                        kartno = g.Key.kartno,
                        personelAdi = g.First().personelAdi,
                        personelSoyadi = g.First().personelSoyadi,
                        GirişSaatleri = g.Where(h => h.IslemTipi == "01")
                                         .OrderBy(h => h.Saat)
                                         .Select(h => h.Saat)
                                         .ToList(),
                        ÇıkışSaatleri = g.Where(h => h.IslemTipi == "02")
                                         .OrderBy(h => h.Saat) // OrderBy kullanarak doğru sıralama
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
                                else if (!(vardiya.serbestcalisma ?? false) && girisSaati < vardiya.ErkenGelme) // SerbestCalisma aktif değilse kontrol et
                                {
                                    hareket.GirişDurumu = "Erken";
                                }
                                else if (!(vardiya.serbestcalisma ?? false) && girisSaati > vardiya.GecGelme) // SerbestCalisma aktif değilse kontrol et
                                {
                                    hareket.GirişDurumu = "Geç";
                                }
                                else
                                {
                                    hareket.GirişDurumu = "OK";
                                }
                            }

                            // Çıkış durumu kontrolü - SerbestCalisma aktif değilse kontrol et
                            if (cikisSaati != null && !(vardiya.serbestcalisma ?? false))
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

                            if (girisSaati != null && cikisSaati != null)
                            {
                                TimeSpan girisSaatiValue = (TimeSpan)girisSaati;
                                TimeSpan cikisSaatiValue = (TimeSpan)cikisSaati;

                                if (vardiya.serbestcalisma ?? false)
                                {
                                    // SERBEST ÇALIŞMA DURUMUNDA HESAPLAMA
                                    // Giriş ve çıkış saatlerini direk baz alarak hesaplama 

                                    // Çalışma süresi hesaplama (direkt giriş-çıkış farkı)
                                    hareket.CalismaSuresi = cikisSaatiValue - girisSaatiValue;
                                    if (hareket.CalismaSuresi < TimeSpan.Zero)
                                    {
                                        hareket.CalismaSuresi = hareket.CalismaSuresi.Add(TimeSpan.FromHours(24));
                                    }

                                    // Vardiya süresini hesaplama
                                    var vardiyaSuresi = vardiya.CalismaBitis - vardiya.CalismaBaslangic;
                                    if (vardiyaSuresi < TimeSpan.Zero)
                                    {
                                        vardiyaSuresi = vardiyaSuresi.Add(TimeSpan.FromHours(24));
                                    }

                                    // MCS'ye vardiya süresini atıyoruz (sabit 8 saat yerine)
                                    hareket.MCS = vardiyaSuresi;

                                    // Eksik süre hesaplama
                                    if (hareket.CalismaSuresi < vardiyaSuresi)
                                    {
                                        hareket.EksikSure = vardiyaSuresi - hareket.CalismaSuresi;
                                        // Vardiya süresini doldurmadıysa fazla mesai vermiyoruz
                                        hareket.FazlaMesai = TimeSpan.Zero;
                                    }
                                    else
                                    {
                                        hareket.EksikSure = TimeSpan.Zero;

                                        // Vardiya süresini doldurduysa ve aştıysa fazla mesai hesaplıyoruz
                                        TimeSpan fazlaMesai = hareket.CalismaSuresi - vardiyaSuresi;

                                        // fmOpsiyon değeri varsa tolerans uygula
                                        if (vardiya.fmOpsiyon.HasValue && vardiya.fmOpsiyon > 0)
                                        {
                                            // Eğer fazla mesai süresi tolerans süresinden büyükse hesapla
                                            if (fazlaMesai.TotalMinutes >= vardiya.fmOpsiyon.Value)
                                            {
                                                hareket.FazlaMesai = fazlaMesai;
                                            }
                                            else
                                            {
                                                hareket.FazlaMesai = TimeSpan.Zero;
                                            }
                                        }
                                        else
                                        {
                                            hareket.FazlaMesai = fazlaMesai;
                                        }
                                    }
                                }
                                else
                                {
                                    // NORMAL VARDIYA HESAPLAMA
                                    var vardiyaBaslangic = vardiya.CalismaBaslangic;
                                    var vardiyaBitis = vardiya.CalismaBitis;
                                    var erkenGelme = vardiya.ErkenGelme;

                                    // Fazla mesai başlangıcı hesaplama
                                    TimeSpan fazlaMesaiBaslangic = vardiya.MesaiBaslangic != TimeSpan.Zero ?
                                        vardiya.MesaiBaslangic : vardiyaBitis;

                                    // Başlangıçta fazla mesai değerlerini sıfırlayalım
                                    TimeSpan fazlaMesai1 = TimeSpan.Zero; // Çıkış sonrası fazla mesai
                                    TimeSpan fazlaMesai3 = TimeSpan.Zero; // Erken mesai

                                    // ErkenMesai özelliği etkinse ve personel erken geldiyse
                                    if (vardiya.ErkenMesai == true && girisSaatiValue < vardiyaBaslangic)
                                    {
                                        // Erken gelme süresini hesapla (vardiya başlangıcından ne kadar önce geldi)
                                        TimeSpan erkenGelmeSuresi = erkenGelme - girisSaatiValue;
                                        if (erkenGelmeSuresi < TimeSpan.Zero)
                                        {
                                            erkenGelmeSuresi = erkenGelmeSuresi.Add(TimeSpan.FromHours(24));
                                        }

                                        // Erken gelme süresini FazlaMesai3'e ekle
                                        fazlaMesai3 = erkenGelmeSuresi;
                                        hareket.FazlaMesai3 = fazlaMesai3;

                                        TimeSpan efektifGirisSaati = girisSaatiValue;
                                        if (girisSaatiValue < vardiyaBaslangic)
                                        {
                                            efektifGirisSaati = vardiyaBaslangic;
                                        }

                                        // Ama çıkış saati vardiya bitişinden sonra ise, vardiya bitişini kullan
                                        TimeSpan efektifCikisSaati = cikisSaatiValue;
                                        if (cikisSaatiValue > vardiyaBitis)
                                        {
                                            efektifCikisSaati = vardiyaBitis;
                                        }

                                        hareket.CalismaSuresi = efektifCikisSaati - efektifGirisSaati;
                                    }
                                    else
                                    {
                                        // Standart çalışma süresi hesaplama - vardiya saatleri ile sınırlandırılmış
                                        TimeSpan efektifGirisSaati = girisSaatiValue;
                                        TimeSpan efektifCikisSaati = cikisSaatiValue;

                                        // Eğer vardiya başlangıcından önce geldiyse, vardiya başlangıcını kullan
                                        if (girisSaatiValue < vardiyaBaslangic)
                                        {
                                            efektifGirisSaati = vardiyaBaslangic;
                                        }

                                        // Eğer vardiya bitişinden sonra çıktıysa, vardiya bitişini kullan
                                        if (cikisSaatiValue > vardiyaBitis)
                                        {
                                            efektifCikisSaati = vardiyaBitis;
                                        }

                                        // Çalışma süresini efektif giriş ve çıkış saatleri ile hesapla
                                        hareket.CalismaSuresi = efektifCikisSaati - efektifGirisSaati;

                                        // FazlaMesai3'ü sıfırla
                                        hareket.FazlaMesai3 = TimeSpan.Zero;
                                    }

                                    // Gece vardiyası kontrolü
                                    if (hareket.CalismaSuresi < TimeSpan.Zero)
                                    {
                                        hareket.CalismaSuresi = hareket.CalismaSuresi.Add(TimeSpan.FromHours(24));
                                    }

                                    // Vardiya süresini hesaplama
                                    var vardiyaSuresi = vardiyaBitis - vardiyaBaslangic;
                                    if (vardiyaSuresi < TimeSpan.Zero)
                                    {
                                        vardiyaSuresi = vardiyaSuresi.Add(TimeSpan.FromHours(24));
                                    }

                                    // Eksik çalışma süresini hesaplama - vardiya süresi ile çalışma süresi arasındaki fark
                                    if (vardiyaSuresi > hareket.CalismaSuresi)
                                    {
                                        // Eksik süre = Vardiya süresi - Çalışma süresi
                                        hareket.EksikSure = vardiyaSuresi - hareket.CalismaSuresi;
                                    }
                                    else
                                    {
                                        hareket.EksikSure = TimeSpan.Zero;
                                    }

                                    // MCS (Normal Çalışma Süresi) hesaplama - Vardiya başlangıç ve bitişi arasındaki fark
                                    TimeSpan MCS = vardiyaBitis - vardiyaBaslangic;
                                    if (MCS < TimeSpan.Zero)
                                    {
                                        MCS = MCS.Add(TimeSpan.FromHours(24));
                                    }

                                    hareket.MCS = MCS;

                                    // Çıkış sonrası fazla mesai hesaplama (FazlaMesai1) - personel ne zaman çıktıysa o saati baz alarak hesaplıyoruz
                                    if (cikisSaatiValue > fazlaMesaiBaslangic)
                                    {
                                        TimeSpan gecenSure = cikisSaatiValue - fazlaMesaiBaslangic;
                                        if (gecenSure < TimeSpan.Zero)
                                        {
                                            gecenSure = gecenSure.Add(TimeSpan.FromHours(24));
                                        }

                                        if (vardiya.fmOpsiyon.HasValue && vardiya.fmOpsiyon > 0)
                                        {
                                            // Eğer geçen süre tolerans süresini aşmışsa fazla mesaiye ekle
                                            if (gecenSure.TotalMinutes >= vardiya.fmOpsiyon.Value)
                                            {
                                                fazlaMesai1 = gecenSure;
                                            }
                                        }
                                        else
                                        {
                                            fazlaMesai1 = gecenSure;
                                        }
                                    }

                                    // FazlaMesai1 değerini atama
                                    hareket.FazlaMesai1 = fazlaMesai1;

                                    // Toplam fazla mesaiyi FazlaMesai1 ve FazlaMesai3'ün toplamı olarak hesapla
                                    hareket.FazlaMesai = fazlaMesai1 + fazlaMesai3;
                                }
                            }
                        }
                    }

                    return hareket;
                })
                .ToList();

            // Tüm tarih aralığını oluştur (boş günler için)
            var allDates = Enumerable.Range(0, (endDate.Date - startDate.Date).Days + 1)
                                  .Select(offset => startDate.AddDays(offset).Date)
                                  .ToList();

            // Personel ve tarih bazlı tüm kombinasyonları oluştur
            var allCombinations = from personel in aktifPersoneller
                                  from tarih in allDates
                                  select new { Personel = personel, Tarih = tarih };


            var completeData = allCombinations.GroupJoin(
      processedHareketler,
      combo => new { PersonelId = combo.Personel.id, Tarih = combo.Tarih },
      hareket => new { PersonelId = hareket.PersonelId, Tarih = hareket.Tarih },
      (combo, hareketGroup) =>
      {
          var hareket = hareketGroup.FirstOrDefault();

          // Hareket varsa doğrudan kullan
          if (hareket != null)
          {
              // Personel bilgilerini doğru şekilde atayalım
              // Eğer hareket içinde personelAdi ve personelSoyadi boşsa personel bilgisinden alalım
              if (string.IsNullOrEmpty(hareket.personelAdi) || string.IsNullOrEmpty(hareket.personelSoyadi))
              {
                  hareket.personelAdi = combo.Personel.adi;
                  hareket.personelSoyadi = combo.Personel.soyadi;
              }
              return hareket;
          }

          // Hareket yoksa boş bir hareket oluştur
          var izin = izinListesi.FirstOrDefault(i => i.Personelıd == combo.Personel.id &&
                                                   i.BaslangicTarihi <= combo.Tarih &&
                                                   i.BitisTarihi >= combo.Tarih);

          var newHareket = new Hareket
          {
              Tarih = combo.Tarih,
              PersonelId = combo.Personel.id,
              kartno = combo.Personel.kartno,
              personelAdi = combo.Personel.adi,
              personelSoyadi = combo.Personel.soyadi,
              GirişSaatleri = new List<TimeSpan>(), // TimeSpan? yerine TimeSpan kullanıyoruz
              ÇıkışSaatleri = new List<TimeSpan>(), // TimeSpan? yerine TimeSpan kullanıyoruz
              Bilgi = combo.Tarih.DayOfWeek == DayOfWeek.Sunday ? "HAFTA TATİLİ" :
                      izin != null ? izin.IzinTuru : "DEVAMSIZ"
          };
          return newHareket;
      })
      .OrderBy(h => h.Tarih)
      .ThenBy(h => h.personelAdi)
      .ThenBy(h => h.personelSoyadi)
      .ToList();

            int headerRow = 1;
            headerRow++; // Başlık satırını bir alt satıra kaydır

            // Başlıklar - G.Durumu ve Ç.Durumu sütunları kaldırıldı
            var headers = new[] { "Tarih", "Gün", "Adı", "Soyadı", "Kart No", "Departman",
                           "Giriş", "Çıkış",
                           "MCS", "ÇalışmaSüresi", "Eksik", "FM-1", "FM-3", "Toplam FM",
                           "Bilgi" };

            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cells[headerRow, i + 1].Value = headers[i];
                worksheet.Cells[headerRow, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                worksheet.Cells[headerRow, i + 1].Style.Font.Bold = true;
                worksheet.Cells[headerRow, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.Gray);
                worksheet.Cells[headerRow, i + 1].Style.Font.Color.SetColor(System.Drawing.Color.White);
            }


            // Verileri ekle
            int row = headerRow + 1; // Veri satırı başlangıç değeri

            // Özellikle tarih ve saat formatları için CultureInfo ayarla
            var culture = new System.Globalization.CultureInfo("tr-TR");
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;

            foreach (var data in completeData)
            {
                var currentRow = worksheet.Cells[row, 1, row, headers.Length]; // Tüm satırı kapsayan aralık

                // Temel bilgiler
                worksheet.Cells[row, 1].Value = data.Tarih.ToString("dd.MM.yyyy"); // Tarih formatı
                worksheet.Cells[row, 2].Value = data.Tarih.ToString("dddd", culture); // Gün adı
                worksheet.Cells[row, 3].Value = data.personelAdi;
                worksheet.Cells[row, 4].Value = data.personelSoyadi;
                worksheet.Cells[row, 5].Value = data.kartno;

                // Departman bilgisini personel listesinden al
                var personel = aktifPersoneller.FirstOrDefault(p => p.id == data.PersonelId);
                worksheet.Cells[row, 6].Value = personel?.departman ?? "";

                // Giriş saati - Sütun 7
                if (data.GirişSaatleri.Any())
                {
                    var ilkGiris = data.GirişSaatleri.First();
                    worksheet.Cells[row, 7].Value = ilkGiris.ToString(@"hh\:mm");

                    // Giriş durumuna göre hücre renklendirme
                    if (data.GirişDurumu == "Erken")
                    {
                        worksheet.Cells[row, 7].Style.Font.Color.SetColor(System.Drawing.Color.Blue);
                        worksheet.Cells[row, 7].Style.Font.Bold = true;
                    }
                    else if (data.GirişDurumu == "Geç")
                    {
                        worksheet.Cells[row, 7].Style.Font.Color.SetColor(System.Drawing.Color.Red);
                        worksheet.Cells[row, 7].Style.Font.Bold = true;
                    }
                }
                else
                {
                    worksheet.Cells[row, 7].Value = ""; // Boş bırak
                }

                // Çıkış saati - Sütun 8
                if (data.ÇıkışSaatleri.Any())
                {
                    var sonCikis = data.ÇıkışSaatleri.Last();
                    worksheet.Cells[row, 8].Value = sonCikis.ToString(@"hh\:mm");

                    // Erken çıkış durumunu kırmızı yap
                    if (data.ÇıkışDurumu == "ErkenÇıkma")
                    {
                        worksheet.Cells[row, 8].Style.Font.Color.SetColor(System.Drawing.Color.Red);
                        worksheet.Cells[row, 8].Style.Font.Bold = true;
                    }
                }
                else
                {
                    worksheet.Cells[row, 8].Value = ""; // Boş bırak
                }

                // MCS (Mecburi Çalışma Süresi) - Sütun 9
                if (data.MCS != TimeSpan.Zero)
                {
                    worksheet.Cells[row, 9].Value = data.MCS.ToString(@"hh\:mm");
                }

                else
                {
                    worksheet.Cells[row, 9].Value = "";
                }

                // Çalışma Süresi - Sütun 10
                if (data.CalismaSuresi != null)
                {
                    worksheet.Cells[row, 10].Value = data.CalismaSuresi.ToString(@"hh\:mm");

                    // Çalışma süresi MCS'den küçükse kırmızıya boyayalım
                    if (data.MCS != null && data.CalismaSuresi < data.MCS)
                    {
                        worksheet.Cells[row, 10].Style.Font.Color.SetColor(System.Drawing.Color.Red);
                        worksheet.Cells[row, 10].Style.Font.Bold = true;
                    }
                }

                else
                {
                    worksheet.Cells[row, 10].Value = "";
                }

                // Eksik Süre - Sütun 11
                if (data.EksikSure != null && data.EksikSure > TimeSpan.Zero)
                {
                    worksheet.Cells[row, 11].Value = data.EksikSure.ToString(@"hh\:mm");
                    worksheet.Cells[row, 11].Style.Font.Color.SetColor(System.Drawing.Color.Red);
                    worksheet.Cells[row, 11].Style.Font.Bold = true;
                }
                else
                {
                    worksheet.Cells[row, 11].Value = "";
                }


                // Fazla Mesai Süresi - Sütun 12
                if (data.FazlaMesai != null && data.FazlaMesai > TimeSpan.Zero)
                {
                    worksheet.Cells[row, 12].Value = data.FazlaMesai1.ToString(@"hh\:mm");
                    worksheet.Cells[row, 12].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[row, 12].Style.Fill.BackgroundColor.SetColor(ColorTranslator.FromHtml("#00fe00"));
                    worksheet.Cells[row, 12].Style.Font.Bold = true;
                }
                else
                {
                    worksheet.Cells[row, 12].Value = "";
                }


                // Erken Mesai (FazlaMesai3) - Sütun 13
                if (data.FazlaMesai3 != null && data.FazlaMesai3 > TimeSpan.Zero)
                {
                    worksheet.Cells[row, 13].Value = data.FazlaMesai3.ToString(@"hh\:mm");
                    worksheet.Cells[row, 13].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[row, 13].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightBlue);
                    worksheet.Cells[row, 13].Style.Font.Bold = true;
                }
                else
                {
                    worksheet.Cells[row, 13].Value = "";
                }
                // Toplam Fazla Mesai (FazlaMesai = FazlaMesai1 + FazlaMesai3) - Sütun 14
                if (data.FazlaMesai != null && data.FazlaMesai > TimeSpan.Zero)
                {
                    worksheet.Cells[row, 14].Value = data.FazlaMesai.ToString(@"hh\:mm");
                    worksheet.Cells[row, 14].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[row, 14].Style.Fill.BackgroundColor.SetColor(ColorTranslator.FromHtml("#ffcc00")); // Sarı renk
                    worksheet.Cells[row, 14].Style.Font.Bold = true;
                }
                else
                {
                    worksheet.Cells[row, 14].Value = "";
                }


                // Bilgi - Sütun 15
                worksheet.Cells[row, 15].Value = data.Bilgi;

                // Satır için durum tabanlı biçimlendirme
                bool isPazar = data.Tarih.DayOfWeek == DayOfWeek.Sunday;
                bool isCumartesi = data.Tarih.DayOfWeek == DayOfWeek.Saturday;
                bool isIzin = data.Bilgi != "DEVAMSIZ" &&
                              data.Bilgi != "HAFTA TATİLİ" &&
                              data.Bilgi != "OK" &&
                              data.Bilgi != "ÇIKIŞ YOK" &&
                              data.Bilgi != "GİRİŞ YOK";

                if (isPazar)
                {
                    // Pazar günleri turuncu renk
                    currentRow.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    currentRow.Style.Fill.BackgroundColor.SetColor(ColorTranslator.FromHtml("#fe9900"));
                }
                else if (isCumartesi)
                {
                    // Cumartesi günleri için açık turuncu renk
                    currentRow.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    currentRow.Style.Fill.BackgroundColor.SetColor(ColorTranslator.FromHtml("#f4b084"));
                }
                else if (isIzin)
                {
                    // İzinli günler yeşil
                    currentRow.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    currentRow.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGreen);
                }
                else if (data.Bilgi == "DEVAMSIZ")
                {
                    // Devamsız günler pembe
                    currentRow.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    currentRow.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightPink);
                    currentRow.Style.Font.Color.SetColor(System.Drawing.Color.Red);
                }

                // Giriş durumu "Geç" ise satırı sarıya boya (diğer renklendirmelerden daha öncelikli)
                if (data.GirişDurumu == "Geç" && !isPazar && !isCumartesi && !isIzin && data.Bilgi != "DEVAMSIZ")
                {
                    currentRow.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    currentRow.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.Yellow);
                }

                // Hücre kenarlıkları
                currentRow.Style.Border.Top.Style = currentRow.Style.Border.Bottom.Style =
                currentRow.Style.Border.Left.Style = currentRow.Style.Border.Right.Style = ExcelBorderStyle.Thin;

                row++;
            }
            // Sütun genişliklerini ayarla
            worksheet.Column(1).Width = 15;  // Tarih
            worksheet.Column(2).Width = 15;  // Gün
            worksheet.Column(3).Width = 15;  // Ad
            worksheet.Column(4).Width = 15;  // Soyad
            worksheet.Column(5).Width = 15;  // Kart No
            worksheet.Column(6).Width = 15;  // Departman
            worksheet.Column(7).Width = 10;  // Giriş
            worksheet.Column(8).Width = 10;  // Çıkış
            worksheet.Column(9).Width = 10;  // MCS
            worksheet.Column(10).Width = 15; // Çalışma Süresi 
            worksheet.Column(11).Width = 10; // Eksik
            worksheet.Column(12).Width = 10; // FM-1
            worksheet.Column(13).Width = 10; // FM-3
            worksheet.Column(14).Width = 10; // Toplam FM
            worksheet.Column(15).Width = 15; // Bilgi

            // Otomatik filtreleme ekle
            worksheet.Cells[1, 1, 1, 15].AutoFilter = true;

            return package;
        }
        private TimeSpan GetFazlaMesaiBaslangici(dynamic vardiya)
        {
            if (vardiya == null)
                return TimeSpan.Zero;

            // Fazla mesai başlangıcını belirle
            TimeSpan fazlaMesaiBaslangic;

            // Eğer MesaiBaslangic tanımlanmışsa onu kullan
            if (vardiya.MesaiBaslangic != TimeSpan.Zero)
            {
                fazlaMesaiBaslangic = vardiya.MesaiBaslangic;
            }
            else
            {
                // MesaiBaslangic tanımlı değilse vardiya bitişini kullan
                fazlaMesaiBaslangic = vardiya.CalismaBitis;
            }

            // fmOpsiyon değerine göre tolerans süresini ekle
            if (vardiya.fmOpsiyon.HasValue && vardiya.fmOpsiyon.Value > 0)
            {
                return fazlaMesaiBaslangic.Add(TimeSpan.FromMinutes(vardiya.fmOpsiyon.Value));
            }
            else
            {
                // Varsayılan olarak 45 dakika ekle
                return fazlaMesaiBaslangic.Add(TimeSpan.FromMinutes(0));
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