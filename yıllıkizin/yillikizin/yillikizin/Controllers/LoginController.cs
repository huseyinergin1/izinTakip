using System.Linq;
using System.Web.Mvc;
using yillikizin.Models; // Model referansı

namespace yillikizin.Controllers
{
    public class LoginController : Controller
    {
        private YillikizinEntities db = new YillikizinEntities(); // Veritabanı bağlamı

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

                // Kullanıcı başarılıysa, ana sayfaya yönlendir
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
