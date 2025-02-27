using System;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Web.Mvc;
using yillikizin.Models;

namespace yillikizin.Controllers
{
    [OzelYetki]
    public class CihazController : Controller
    {
        private YillikizinEntities db = new YillikizinEntities();
        
        public ActionResult Index()
        {
            var cihazlar = db.Cihazlar.ToList();

            // Bağlantı durumu güncelleme
            foreach (var cihaz in cihazlar)
            {
                // Eğer Port değeri varsa, TestConnection metoduna geçerli int değeri ile iletin
                if (cihaz.Port.HasValue)
                {
                    cihaz.IsConnected = TestConnection(cihaz.IpAdresi, cihaz.Port.Value); // .Value ile nullable int'i int'e dönüştür
                }
                else
                {
                    cihaz.IsConnected = false; // Port değeri yoksa bağlantı yok olarak kabul edelim
                }
            }

            db.SaveChanges(); // Güncellenmiş bağlantı durumlarını kaydet

            return View(cihazlar);
        }

        public ActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(Cihazlar cihaz)
        {
            if (ModelState.IsValid)
            {
                db.Cihazlar.Add(cihaz);
                db.SaveChanges();
                return RedirectToAction("Index");
            }
            return View(cihaz);
        }

        // GET: Cihaz/Edit/5
        public ActionResult Edit(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            Cihazlar cihaz = db.Cihazlar.Find(id);
            if (cihaz == null)
            {
                return HttpNotFound();
            }
            return View(cihaz);
        }

        // POST: Cihaz/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(Cihazlar cihaz)
        {
            if (ModelState.IsValid)
            {
                var mevcutCihaz = db.Cihazlar.Find(cihaz.Id);
                if (mevcutCihaz == null)
                {
                    return HttpNotFound(); // Cihaz bulunamazsa hata döndür
                }

                // Mevcut cihaz bilgilerini güncelle
                mevcutCihaz.Model = cihaz.Model;
                mevcutCihaz.IpAdresi = cihaz.IpAdresi;
                mevcutCihaz.Port = cihaz.Port;

                // Port null ise bağlantı testi yapılmaz
                if (cihaz.Port.HasValue)
                {
                    // Bağlantı testi
                    bool baglantiDurumu = TestConnection(cihaz.IpAdresi, cihaz.Port.Value); // .Value ile nullable'dan int'e erişilir
                    mevcutCihaz.IsConnected = baglantiDurumu;
                }
                else
                {
                    mevcutCihaz.IsConnected = false; // Port null ise bağlantı durumu false
                }

                db.Entry(mevcutCihaz).State = EntityState.Modified;
                db.SaveChanges();

                return RedirectToAction("Index"); // Başarılı güncelleme sonrası yönlendirme
            }

            return View(cihaz); // Model doğrulama başarısızsa aynı sayfaya dön
        }

        // Bağlantı testi yapmak için yöntem
        private bool TestConnection(string ipAddress, int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    // Bağlantı denemesini başlat
                    var result = client.BeginConnect(ipAddress, port, null, null);

                    // Zaman aşımı ayarla (1 saniye)
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2));

                    if (!success)
                    {
                        throw new SocketException(); // Bağlantı süresi aşıldıysa hata fırlat
                    }

                    // Bağlantıyı tamamla
                    client.EndConnect(result);

                    return true; // Bağlantı başarılı
                }
            }
            catch (Exception)
            {
                return false; // Bağlantı hatası
            }
        }

        [HttpGet]
        public JsonResult RefreshConnections()
        {
            var cihazlar = db.Cihazlar.ToList(); // Cihaz listesini al

            // Her cihaz için bağlantı durumunu kontrol et
            foreach (var cihaz in cihazlar)
            {
                if (!string.IsNullOrEmpty(cihaz.IpAdresi) && cihaz.Port.HasValue)
                {
                    cihaz.IsConnected = TestConnection(cihaz.IpAdresi, cihaz.Port.Value); // TestConnection metodunu kullan
                }
                else
                {
                    cihaz.IsConnected = false; // IP veya port eksikse bağlantı yok olarak işaretle
                }
            }

            db.SaveChanges(); // Bağlantı durumlarını veritabanına kaydet

            // JSON formatında döndürmek için cihaz bilgilerini hazırlayın
            var cihazList = cihazlar.Select(c => new
            {
                c.Id,
                c.Model,
                c.IpAdresi,
                c.Port,
                IsConnected = c.IsConnected ?? false // Nullable boolean için varsayılan değer kullan
            }).ToList();

            return Json(cihazList, JsonRequestBehavior.AllowGet); // JSON sonucu döndür
        }

        public ActionResult Delete(int id)
        {
            var cihaz = db.Cihazlar.Find(id);
            if (cihaz != null)
            {
                db.Cihazlar.Remove(cihaz);
                db.SaveChanges();
            }
            return RedirectToAction("Index");
        }
    }
}