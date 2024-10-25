using System.Linq;
using System.Web.Mvc;
using yillikizin.Models; // Model referansı

namespace yillikizin.Controllers
{
    public class LoginController : Controller
    {
        private YillikizinEntities db = new YillikizinEntities(); // Veritabanı bağlamı

        // GET: Login
        public ActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Index(string KullaniciAdi, string Sifre)
        {

            // Kullanıcı adı ve şifreyi kontrol et
            var user = db.personel.FirstOrDefault(u => u.kullaniciadi == KullaniciAdi && u.sifre == Sifre);



            if (user != null)
            {
                // Kullanıcının bilgilerini sessiona at
                HttpContext.Session["KullaniciAdi"] = user.kullaniciadi;
                HttpContext.Session["Sifre"] = user.sifre;
                HttpContext.Session["Name"] = user.adi;
                HttpContext.Session["Surname"] = user.soyadi;
                HttpContext.Session["UserId"] = user.id;
                HttpContext.Session["UserKartno"] = user.kartno;
                HttpContext.Session["UserSicilno"] = user.sicilno;
                HttpContext.Session["UserDepartman"] = user.departman;
                HttpContext.Session["UserUnvan"] = user.unvan;
                HttpContext.Session["UserDogum"] = user.dogumtarih;
                HttpContext.Session["UserImage"] = user.resim;
                HttpContext.Session["UserKalan"] = user.kalan;
                HttpContext.Session["UserKullandigi"] = user.kullandigi;
                HttpContext.Session["UserHakettigi"] = user.hakettigi;
                HttpContext.Session["UserIsegiris"] = user.isegiristarih;
                // Kullanıcı varsa, oturum aç ve ana sayfaya yönlendir
                Session["UserId"] = user.id;
                return RedirectToAction("Index", "Home");
            }
            else
            {
                // Kullanıcı yoksa, hata mesajı ekle ve aynı sayfada kal
                ModelState.AddModelError("", "Kullanıcı adı veya şifre hatalı.");
                return View();
            }
        }

    }
}
