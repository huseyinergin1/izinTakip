using yillikizin.Models;
using System;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.IO;
using System.Web.Helpers;
using System.Collections.Generic;
using yillikizin.Filters;
using System.Data.Entity;

namespace yillikizin.Controllers
{
    [CustomAuthorize]
    public class HomeController : Controller
    {
        YillikizinEntities db = new YillikizinEntities();
        public ActionResult Index(int hareketSayisi = 20)
        {
            // Kullanıcı tarafından belirlenen hareket sayısını saklamak için
            int defaultHareketSayisi = 20; // Varsayılan değeri 20
            if (Request.Cookies["hareketSayisi"] != null)
            {
                int.TryParse(Request.Cookies["hareketSayisi"].Value, out defaultHareketSayisi);
            }

            hareketSayisi = hareketSayisi != 0 ? hareketSayisi : defaultHareketSayisi;
            // Toplam personel sayısı
            ViewBag.TotalPersonel = db.personel.Count();

            // Departman sayısı
            ViewBag.DepartmanSayisi = db.departman.Count();

            // Bugün izinli olan personeller
            var today = DateTime.Now.Date;
            var izinliPersonelList = db.Izin
                .Where(i => i.BaslangicTarihi <= today && i.BitisTarihi >= today)
                .Select(i => i.personel)
                .Distinct()
                .ToList();

            ViewBag.IzinliPersonel = izinliPersonelList.Count;
            ViewBag.IzinliPersonelList = izinliPersonelList;

            // Bugün doğum günü olan personeller
            ViewBag.DogumGunuOlanlar = db.personel
                .Where(p => p.dogumtarih.HasValue &&
                            p.dogumtarih.Value.Month == today.Month &&
                            p.dogumtarih.Value.Day == today.Day)
                .ToList();

            // Son hareket sayısını ViewBag'e ekleyin
            ViewBag.HareketSayisi = hareketSayisi;

            // Son hareketleri veritabanından al
            ViewBag.SonHareketler = ReadLastMovements(hareketSayisi);

            // Tüm personel bilgilerini al
            ViewBag.Personeller = db.personel.ToList();
            ViewBag.Departmanlar = db.departman.ToList();
            ViewBag.DevamsizPersoneller = db.personel.ToList();
            ViewBag.IseGelenPersonelSayisi = db.personel.ToList();

            // Departman başına personel sayısını hesapla
            var departmanPersonelSayilari = db.departman.ToDictionary(
                d => d.departmanId,
                d => db.personel.Count(p => p.departmanId == d.departmanId)
            );

            ViewBag.DepartmanPersonelSayilari = departmanPersonelSayilari;

            // Devamsız personel kontrolü (ad, soyad ve kart no ile birlikte)
            var devamsizPersonelList = GetDevamsizPersonelWithDetails(today);
            ViewBag.DevamsizPersonelList = devamsizPersonelList;
            ViewBag.DevamsizPersonelSayisi = devamsizPersonelList.Count;

            // İşe gelen personel listesi
            var iseGelenPersonelList = GetIseGelenPersonelWithDetails(today);
            ViewBag.IseGelenPersonelSayisi = iseGelenPersonelList.Count; // İşe gelen sayısını alıyoruz
            ViewBag.IseGelenPersonelList = iseGelenPersonelList;

            return View();
        }

        // Belirtilen sayıda hareketi veritabanından al
        private List<Hareket> ReadLastMovements(int hareketSayisi)
        {
            // Belirtilen sayıda hareketi veritabanından alıyoruz
            var hareketler = (from h in db.Hareketler
                              join p in db.personel on h.KartNumarasi equals p.kartno
                              orderby h.Tarih descending
                              select new Hareket
                              {
                                  kartno = h.KartNumarasi,
                                  TerminalNo = h.TerminalNo,
                                  Tarih = h.Tarih ?? DateTime.MinValue,
                                  Saat = h.Saat ?? TimeSpan.Zero,
                                  IslemTipi = h.Yon,
                                  personelAdi = p.adi,
                                  personelSoyadi = p.soyadi,
                                  departman = p.departman,
                                  GirişSaatleri = new List<TimeSpan> { h.Saat ?? TimeSpan.Zero },
                                  ÇıkışSaatleri = new List<TimeSpan> { h.Saat ?? TimeSpan.Zero }
                              }).Take(hareketSayisi).ToList();

            return hareketler;
        }
        // Devamsız personeli kontrol et
        private List<personel> GetDevamsizPersonelWithDetails(DateTime today)
        {
            var devamsizPersonel = new List<personel>();

            // Veritabanındaki hareketleri al
            var hareketler = db.Hareketler
                .Where(h => h.Tarih == today)
                .Select(h => h.KartNumarasi)
                .ToList();

            // Bugün hareketi olmayan personelleri al
            var tumPersoneller = db.personel.ToList();

            foreach (var personel in tumPersoneller)
            {
                if (!hareketler.Contains(personel.kartno))
                {
                    devamsizPersonel.Add(personel); // Devamsız personel
                }
            }

            return devamsizPersonel; // Devamsız personel listesini döndür
        }
        // İşe gelen personel listesini al
        private List<personel> GetIseGelenPersonelWithDetails(DateTime today)
        {
            var iseGelenPersonel = new List<personel>();

            // Veritabanındaki hareketleri al
            var hareketler = db.Hareketler
                .Where(h => h.Tarih == today)
                .Select(h => h.KartNumarasi)
                .ToList();

            // Bugün hareket yapan personelleri al
            var tumPersoneller = db.personel.ToList();

            foreach (var personel in tumPersoneller)
            {
                if (hareketler.Contains(personel.kartno))
                {
                    iseGelenPersonel.Add(personel); // İşe gelen personel
                }
            }

            return iseGelenPersonel; // İşe gelen personel listesini döndür
        }
        public ActionResult PersonelEkle()
        {
            try
            {
                // Yeni bir personel modeli oluştur
                var model = new personel();

                // Veritabanından departmanları çek
                var departmanlar = db.departman.Select(d => new SelectListItem
                {
                    Value = d.departmanId.ToString(),
                    Text = d.departmanName
                }).ToList();

                // Veritabanından kullanıcı gruplarını çek
                var kullaniciGruplari = db.kullanici_grup.Select(g => new SelectListItem
                {
                    Value = g.kullaniciGrupId.ToString(),
                    Text = g.grupAdi
                }).ToList();

                // Vardiyaları çek
                var vardiyalar = db.Vardiya.Select(v => new SelectListItem
                {
                    Value = v.VardiyaId.ToString(),
                    Text = v.Ad
                }).ToList();

                // ViewBag'leri doldur
                ViewBag.DepartmanList = departmanlar;
                ViewBag.KullaniciGrupList = kullaniciGruplari;
                ViewBag.VardiyaList = vardiyalar;

                // Model ile birlikte view'ı döndür
                return View(model);
            }
            catch (Exception ex)
            {
                // Hata durumunda log tutabilir veya hata sayfasına yönlendirebilirsiniz
                return RedirectToAction("Error", "Home", new { message = ex.Message });
            }
        }
        [HttpPost]
        public ActionResult PersonelEkle(personel Personel, HttpPostedFileBase File)
        {
            // Mevcut kullanıcı adı, kart numarası ve sicil numarasını kontrol et
            var mevcutKullanici = db.personel.FirstOrDefault(p => p.kullaniciadi == Personel.kullaniciadi);
            var mevcutKartNo = db.personel.FirstOrDefault(p => p.kartno == Personel.kartno);
            var mevcutSicilNo = db.personel.FirstOrDefault(p => p.sicilno == Personel.sicilno);

            if (mevcutKullanici != null)
            {
                ModelState.AddModelError("kullaniciadi", "Bu kullanıcı adı zaten alınmış.");
            }
            if (mevcutKartNo != null)
            {
                ModelState.AddModelError("kartno", "Bu kart numarası zaten alınmış.");
            }
            if (mevcutSicilNo != null)
            {
                ModelState.AddModelError("sicilno", "Bu sicil numarası zaten alınmış.");
            }

            if (!ModelState.IsValid)
            {
                var departmanlar = db.departman.ToList();
                ViewBag.DepartmanList = new SelectList(departmanlar, "departmanId", "departmanName");

                var kullaniciGruplari = db.kullanici_grup.ToList();
                ViewBag.KullaniciGrupList = new SelectList(kullaniciGruplari, "kullaniciGrupId", "grupAdi");
                
                var vardiyalar = db.Vardiya.ToList();
                ViewBag.VardiyaList = new SelectList(db.Vardiya, "vardiyaId", "vardiyaAdi");
                return View(Personel);
            }

            // Kart numarasını 10 haneye tamamla
            // Kart numarasını string'e çeviriyoruz
            string kartNoString = Personel.kartno; // artık string olarak alıyoruz
                                                   // PadLeft kullanarak 10 haneye tamamlıyoruz
            kartNoString = kartNoString?.PadLeft(10, '0');

            // Personel modeline kart numarasını atıyoruz
            Personel.kartno = kartNoString; // kartno artık string

            // Resim yükleme işlemi
            if (File != null)
            {
                FileInfo fileinfo = new FileInfo(File.FileName);
                WebImage img = new WebImage(File.InputStream);
                string uzanti = (Guid.NewGuid().ToString() + fileinfo.Extension).ToLower();
                img.Resize(200, 180, false, false);
                string tamyol = "~/images/users/" + uzanti;
                img.Save(Server.MapPath(tamyol));
                Personel.resim = "/images/users/" + uzanti;
            }
            else
            {
                Personel.resim = "/images/users/user.png"; // Varsayılan resim
            }

            // Seçilen departmanId'yi personel modeline ata
            Personel.departmanId = int.Parse(Request.Form["departmanId"]);

            // Seçilen kullanıcı grup Id'sini personel modeline ata
            Personel.kullaniciGrupId = int.Parse(Request.Form["kullaniciGrupId"]);

            Personel.VardiyaId = int.Parse(Request.Form["vardiyaId"]);
            // Departman adı almak için departmanId ile karşılaştırma yap
            var departmanName = db.departman
                .Where(d => d.departmanId == Personel.departmanId)
                .Select(d => d.departmanName)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(departmanName))
            {
                Personel.departman = departmanName;
            }

            // Kullanıcı grubu adı almak için kullaniciGrupId ile karşılaştırma yap
            var grupAdi = db.kullanici_grup
                .Where(g => g.kullaniciGrupId == Personel.kullaniciGrupId)
                .Select(g => g.grupAdi)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(grupAdi))
            {
                Personel.kullaniciGrupAdi = grupAdi; // Personel tablosundaki grupAdi alanına yaz
            }

            // Yeni personeli ekle
            db.personel.Add(Personel);
            db.SaveChanges();

            return RedirectToAction("Index");
        }
        public ActionResult Departman()
        {
            ViewBag.Title = "Yıllık İzin | 2024";

            // Tüm departmanları listele
            var departmanlar = db.departman.ToList();
            return View(departmanlar);
        }

        // Departman Ekleme İşlemi (POST)
        [HttpPost]
        public ActionResult DepartmanEkle(string DepartmanAdi)
        {
            if (!string.IsNullOrEmpty(DepartmanAdi))
            {
                // Yeni departmanı oluştur
                departman yeniDepartman = new departman
                {
                    departmanName = DepartmanAdi
                };

                // Veritabanına ekle
                db.departman.Add(yeniDepartman);
                db.SaveChanges();

                // Başarılı ekleme sonrası aynı sayfaya geri dön
                return RedirectToAction("Departman");
            }

            // Departman adı boşsa hata mesajı ekleyip formu tekrar göster
            ModelState.AddModelError("", "Departman adı boş olamaz.");
            return View("Departman");
        }

        // Departman Silme İşlemi
        public ActionResult DepartmanSil(int id)
        {
            var departman = db.departman.Find(id);
            if (departman != null)
            {
                db.departman.Remove(departman);
                db.SaveChanges();
            }

            return RedirectToAction("Departman");
        }

        // Departman Düzenleme Sayfası (GET)
        public ActionResult DepartmanDuzenle(int id)
        {
            var departman = db.departman.Find(id);
            if (departman == null)
            {
                return HttpNotFound();
            }

            return View(departman);
        }

        // Departman Düzenleme İşlemi (POST)
        [HttpPost]
        public ActionResult DepartmanDuzenle(departman departman)
        {
            if (ModelState.IsValid)
            {
                // Veritabanında güncelle
                db.Entry(departman).State = System.Data.Entity.EntityState.Modified;
                db.SaveChanges();

                return RedirectToAction("Departman");
            }

            return View(departman);
        }

        public ActionResult Contact()
        {

            return View();
        }

    }
}