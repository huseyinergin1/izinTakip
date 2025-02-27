using System.Linq;
using System.Web.Mvc;
using yillikizin.Models; // Model referansı

namespace yillikizin.Controllers
{
    public class LoginController : Controller
    {
        private YillikizinEntities db = new YillikizinEntities();

        // GET: Login
        [AllowAnonymous]
        public ActionResult Index()
        {
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        public ActionResult Index(string KullaniciAdi, string Sifre)
        {
            // Kullanıcı adı ve şifreyi kontrol et
            var user = db.personel.FirstOrDefault(u => u.kullaniciadi == KullaniciAdi && u.sifre == Sifre);

            if (user != null)
            {
                // Çalışma durumu kontrolü
                if (!user.calisma)
                {
                    ModelState.AddModelError("", "Bu hesap devre dışı bırakılmıştır. Sistem yöneticisi ile iletişime geçiniz.");
                    return View();
                }

                // Kullanıcı varsa, bilgilerini session'a at
                Session["KullaniciAdi"] = user.kullaniciadi;
                Session["Sifre"] = user.sifre;
                Session["Name"] = user.adi;
                Session["Surname"] = user.soyadi;
                Session["UserId"] = user.id;
                Session["UserKartno"] = user.kartno;
                Session["UserSicilno"] = user.sicilno;
                Session["UserDepartman"] = user.departman;
                Session["UserUnvan"] = user.unvan;
                Session["UserDogum"] = user.dogumtarih;
                Session["UserImage"] = user.resim;
                Session["UserKalan"] = user.kalan;
                Session["UserKullandigi"] = user.kullandigi;
                Session["UserHakettigi"] = user.hakettigi;
                Session["UserIsegiris"] = user.isegiristarih;

                // Yetkilendirme için kullanıcı grup bilgisini Session'a ekle
                Session["KullaniciGrupId"] = user.kullaniciGrupId;

                // Kullanıcı tipine göre yönlendirme yap
                if (user.kullaniciGrupId == 2) // Normal personel
                {
                    return RedirectToAction("HareketDegerlendirme", "Hareket");
                }
                else // Admin
                {
                    return RedirectToAction("Index", "Home");
                }
            }
            else
            {
                // Kullanıcı yoksa, hata mesajı ekle ve aynı sayfada kal
                ModelState.AddModelError("", "Kullanıcı adı veya şifre hatalı.");
                return View();
            }
        }

        // Çıkış işlemi için yeni metod ekleyelim
        public ActionResult Logout()
        {
            // Tüm session'ları temizle
            Session.Clear();
            Session.Abandon();

            // Login sayfasına yönlendir
            return RedirectToAction("Index", "Login");
        }
    }
}
