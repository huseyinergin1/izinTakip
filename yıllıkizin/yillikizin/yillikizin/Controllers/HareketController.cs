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
                                TimeSpan fazlaMesai3 = TimeSpan.Zero; // Erken mesai (giriş öncesi)

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

                                // MCS (Mecburi Çalışma Süresi) hesaplama - Vardiya başlangıç ve bitişi arasındaki fark
                                TimeSpan MCS = vardiyaBitis - vardiyaBaslangic;
                                if (MCS < TimeSpan.Zero)
                                {
                                    MCS = MCS.Add(TimeSpan.FromHours(24));
                                }

                                hareket.MCS = MCS;

                                // Çıkış sonrası fazla mesai hesaplama (fazlaMesai1)
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

                                // FazlaMesai1 değerini Hareket sınıfında bir özelliğe atayalım (eğer öyle bir özellik yoksa eklemeliyiz)
                                hareket.FazlaMesai1 = fazlaMesai1;

                                // Toplam fazla mesai hesaplama (erken + çıkış sonrası)
                                hareket.FazlaMesai = fazlaMesai1 + fazlaMesai3;
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

        // VardiyaListesi'ni al
        var vardiyaListesi = GetVardiyaListesi();

        // Sadece çalışma durumu true olan personelleri al
        var aktifPersoneller = GetPersonelListesi().Where(p => p.calisma == true).ToList();

        var hareketListesi = GetHareketListesi();
        var izinListesi = GetIzinListesi();

        // Eğer SelectedPersonelId varsa filtreleme yap
        if (SelectedPersonelId.HasValue)
        {
            aktifPersoneller = aktifPersoneller.Where(p => p.id == SelectedPersonelId.Value).ToList();
        }

        // Tarih filtresi uygula
        hareketListesi = hareketListesi
            .Where(h => h.Tarih >= startDate && h.Tarih <= endDate)
            .ToList();

        // Sadece aktif personellerin hareketlerini filtrele
        hareketListesi = hareketListesi
            .Where(h => aktifPersoneller.Any(p => p.kartno == h.kartno))
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

        // Tüm veriler için izinleri kontrol et ve gerekirse işlemleri yap
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

                    // Arama filtrelerine göre kontrol et
                    if ((string.IsNullOrEmpty(adiArama) || hareket.personelAdi.ToLower().Contains(adiArama)) &&
                        (string.IsNullOrEmpty(soyadiArama) || hareket.personelSoyadi.ToLower().Contains(soyadiArama)) &&
                        (string.IsNullOrEmpty(kartArama) || hareket.kartno.ToLower().Contains(kartArama)) &&
                        (string.IsNullOrEmpty(departmanArama) || combo.Personel.departman.ToLower().Contains(departmanArama)) &&
                        (string.IsNullOrEmpty(tarihArama) || combo.Tarih.ToString("yyyy-MM-dd").ToLower().Contains(tarihArama)))
                    {
                        return hareket;
                    }
                    return null;
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

                // Arama filtrelerine göre kontrol et
                if ((string.IsNullOrEmpty(adiArama) || newHareket.personelAdi.ToLower().Contains(adiArama)) &&
                    (string.IsNullOrEmpty(soyadiArama) || newHareket.personelSoyadi.ToLower().Contains(soyadiArama)) &&
                    (string.IsNullOrEmpty(kartArama) || newHareket.kartno.ToLower().Contains(kartArama)) &&
                    (string.IsNullOrEmpty(departmanArama) || combo.Personel.departman.ToLower().Contains(departmanArama)) &&
                    (string.IsNullOrEmpty(tarihArama) || combo.Tarih.ToString("yyyy-MM-dd").ToLower().Contains(tarihArama)))
                {
                    return newHareket;
                }
                return null;
            })
            .Where(h => h != null)
            .OrderBy(h => h.Tarih)
            .ThenBy(h => h.personelAdi)
            .ThenBy(h => h.personelSoyadi)
            .ToList();

        // FilterType'a göre filtreleme
        if (!string.IsNullOrEmpty(FilterType))
        {
            switch (FilterType)
            {
                case "Erken":
                    completeData = completeData.Where(d =>
                        d.GirişSaatleri.Any() &&
                        d.GirişDurumu == "Erken" &&
                        d.Bilgi != "DEVAMSIZ" &&
                        d.Bilgi != "HAFTA TATİLİ"
                    ).ToList();
                    break;
                case "Geç":
                    completeData = completeData.Where(d =>
                        d.GirişSaatleri.Any() &&
                        d.GirişDurumu == "Geç" &&
                        d.Bilgi != "DEVAMSIZ" &&
                        d.Bilgi != "HAFTA TATİLİ"
                    ).ToList();
                    break;
                case "Izinliler":
                    completeData = completeData.Where(d =>
                        d.Bilgi != "DEVAMSIZ" &&
                        d.Bilgi != "HAFTA TATİLİ" &&
                        d.Bilgi != "OK" &&
                        d.Bilgi != "ÇIKIŞ YOK" &&
                        d.Bilgi != "GİRİŞ YOK"
                    ).ToList();
                    break;
                case "Devamsiz":
                    completeData = completeData.Where(d => d.Bilgi == "DEVAMSIZ").ToList();
                    break;
                case "FazlaMesai":
                    completeData = completeData.Where(d =>
                        d.FazlaMesai > TimeSpan.Zero
                    ).ToList();

                    break;
            }
        }

        using (var package = new ExcelPackage())
        {
            var worksheet = package.Workbook.Worksheets.Add("Hareketler");

            // Başlıklardan önce filtre ve tarih bilgisi ekle
            int headerRow = 1;

            // Filtre bilgisi
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
                worksheet.Cells[1, 1, 1, 2].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);

                headerRow = 2; // Başlık satırını bir alt satıra kaydır
            }

            // Tarih aralığı bilgisi
            worksheet.Cells[headerRow, 1].Value = "Tarih Aralığı:";
            worksheet.Cells[headerRow, 2].Value = $"{startDate.ToString("dd.MM.yyyy")} - {endDate.ToString("dd.MM.yyyy")}";
            worksheet.Cells[headerRow, 1].Style.Font.Bold = true;
            worksheet.Cells[headerRow, 1, headerRow, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
            worksheet.Cells[headerRow, 1, headerRow, 2].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);

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

            // Otomatik filtreleme ekle - bir sütun daha eklendiği için 15'e güncelledik
            worksheet.Cells[headerRow, 1, headerRow, 15].AutoFilter = true;
            // Tüm hücreleri otomatik yükseklik ve genişlik ayarı için formatlama
            worksheet.Cells.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            worksheet.Cells.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // Özetleme Bilgileri (İstatistikler)
            int summaryRow = row + 2;
            worksheet.Cells[summaryRow, 1].Value = "ÖZET BİLGİ";
            worksheet.Cells[summaryRow, 1].Style.Font.Bold = true;
            worksheet.Cells[summaryRow, 1].Style.Font.Size = 14;
            worksheet.Cells[summaryRow, 1, summaryRow, 6].Merge = true;
            worksheet.Cells[summaryRow, 1, summaryRow, 6].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            summaryRow += 2;

            // Özet bilgileri hesapla
            int toplamKayit = completeData.Count;
            int gecGelenler = completeData.Count(d => d.GirişDurumu == "Geç");
            int erkenGelenler = completeData.Count(d => d.GirişDurumu == "Erken");
            int erkenCikanlar = completeData.Count(d => d.ÇıkışDurumu == "ErkenÇıkma");
            int fazlaMesaiYapanlar = completeData.Count(d => d.FazlaMesai > TimeSpan.Zero);
            int erkenMesaiYapanlar = completeData.Count(d => d.FazlaMesai3 > TimeSpan.Zero);
            int izinliGunler = completeData.Count(d => d.Bilgi != "DEVAMSIZ" && d.Bilgi != "HAFTA TATİLİ" &&
                                                      d.Bilgi != "OK" && d.Bilgi != "ÇIKIŞ YOK" && d.Bilgi != "GİRİŞ YOK");
            int devamsizGunler = completeData.Count(d => d.Bilgi == "DEVAMSIZ");
            int haftatatiligünler = completeData.Count(d => d.Bilgi == "HAFTA TATİLİ");

            // Toplam mesai süresi
            TimeSpan toplamFazlaMesai = TimeSpan.Zero;
            foreach (var data in completeData.Where(d => d.FazlaMesai != null))
            {
                toplamFazlaMesai = toplamFazlaMesai.Add(data.FazlaMesai);
            }

            // Toplam erken mesai süresi
            TimeSpan toplamErkenMesai = TimeSpan.Zero;
            foreach (var data in completeData.Where(d => d.FazlaMesai3 != null))
            {
                toplamErkenMesai = toplamErkenMesai.Add(data.FazlaMesai3);
            }

            // Toplam erken mesai süresi
            TimeSpan toplamFM1 = TimeSpan.Zero;
            foreach (var data in completeData.Where(d => d.FazlaMesai1 != null))
            {
                toplamFM1 = toplamFM1.Add(data.FazlaMesai1);
            }

            TimeSpan toplamFM3 = TimeSpan.Zero;
            foreach (var data in completeData.Where(d => d.FazlaMesai3 != null))
            {
                toplamFM3 = toplamFM3.Add(data.FazlaMesai3);
            }
            // Özet bilgilerini ekle
            worksheet.Cells[summaryRow, 1].Value = "Toplam Kayıt Sayısı:";
            worksheet.Cells[summaryRow, 2].Value = toplamKayit;
            worksheet.Cells[summaryRow, 1].Style.Font.Bold = true;

            summaryRow++;
            worksheet.Cells[summaryRow, 1].Value = "Geç Gelenler:";
            worksheet.Cells[summaryRow, 2].Value = gecGelenler;
            worksheet.Cells[summaryRow, 1].Style.Font.Bold = true;
            if (gecGelenler > 0) worksheet.Cells[summaryRow, 2].Style.Font.Color.SetColor(System.Drawing.Color.Red);

            summaryRow++;
            worksheet.Cells[summaryRow, 1].Value = "Erken Gelenler:";
            worksheet.Cells[summaryRow, 2].Value = erkenGelenler;
            worksheet.Cells[summaryRow, 1].Style.Font.Bold = true;

            summaryRow++;
            worksheet.Cells[summaryRow, 1].Value = "Erken Çıkanlar:";
            worksheet.Cells[summaryRow, 2].Value = erkenCikanlar;
            worksheet.Cells[summaryRow, 1].Style.Font.Bold = true;
            if (erkenCikanlar > 0) worksheet.Cells[summaryRow, 2].Style.Font.Color.SetColor(System.Drawing.Color.Red);

            summaryRow++;
            worksheet.Cells[summaryRow, 1].Value = "Fazla Mesai Yapanlar:";
            worksheet.Cells[summaryRow, 2].Value = fazlaMesaiYapanlar;
            worksheet.Cells[summaryRow, 1].Style.Font.Bold = true;

            summaryRow++;
            worksheet.Cells[summaryRow, 1].Value = "Erken Mesai Yapanlar:";
            worksheet.Cells[summaryRow, 2].Value = erkenMesaiYapanlar;
            worksheet.Cells[summaryRow, 1].Style.Font.Bold = true;

            summaryRow++;
            worksheet.Cells[summaryRow, 1].Value = "İzinli Günler:";
            worksheet.Cells[summaryRow, 2].Value = izinliGunler;
            worksheet.Cells[summaryRow, 1].Style.Font.Bold = true;

            summaryRow++;
            worksheet.Cells[summaryRow, 1].Value = "Devamsız Günler:";
            worksheet.Cells[summaryRow, 2].Value = devamsizGunler;
            worksheet.Cells[summaryRow, 1].Style.Font.Bold = true;
            if (devamsizGunler > 0) worksheet.Cells[summaryRow, 2].Style.Font.Color.SetColor(System.Drawing.Color.Red);

            summaryRow++;
            worksheet.Cells[summaryRow, 1].Value = "Hafta Tatili Günleri:";
            worksheet.Cells[summaryRow, 2].Value = haftatatiligünler;
            worksheet.Cells[summaryRow, 1].Style.Font.Bold = true;

            summaryRow += 2;
            worksheet.Cells[summaryRow, 1].Value = "Toplam FM-1:";
            worksheet.Cells[summaryRow, 2].Value = toplamFM1.TotalHours >= 24 ?
                $"{Math.Floor(toplamFM1.TotalDays)} gün {toplamFM1.ToString(@"hh\:mm")}" :
                toplamFM1.ToString(@"hh\:mm");
            worksheet.Cells[summaryRow, 1].Style.Font.Bold = true;
            worksheet.Cells[summaryRow, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
            worksheet.Cells[summaryRow, 2].Style.Fill.BackgroundColor.SetColor(ColorTranslator.FromHtml("#00fe00"));

            summaryRow++;
            worksheet.Cells[summaryRow, 1].Value = "Toplam Erken Mesai:";
            worksheet.Cells[summaryRow, 2].Value = toplamErkenMesai.TotalHours >= 24 ?
                $"{Math.Floor(toplamErkenMesai.TotalDays)} gün {toplamErkenMesai.ToString(@"hh\:mm")}" :
                toplamErkenMesai.ToString(@"hh\:mm");
            worksheet.Cells[summaryRow, 1].Style.Font.Bold = true;
            worksheet.Cells[summaryRow, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
            worksheet.Cells[summaryRow, 1].Value = "Toplam FM-3:";
            worksheet.Cells[summaryRow, 2].Value = toplamErkenMesai.TotalHours >= 24 ?
                $"{Math.Floor(toplamErkenMesai.TotalDays)} gün {toplamErkenMesai.ToString(@"hh\:mm")}" :
                toplamErkenMesai.ToString(@"hh\:mm");
            worksheet.Cells[summaryRow, 1].Style.Font.Bold = true;
            worksheet.Cells[summaryRow, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
            worksheet.Cells[summaryRow, 2].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightBlue);

            summaryRow++;
            worksheet.Cells[summaryRow, 1].Value = "Toplam FM:";
            worksheet.Cells[summaryRow, 2].Value = toplamFazlaMesai.TotalHours >= 24 ?
                $"{Math.Floor(toplamFazlaMesai.TotalDays)} gün {toplamFazlaMesai.ToString(@"hh\:mm")}" :
                toplamFazlaMesai.ToString(@"hh\:mm");
            worksheet.Cells[summaryRow, 1].Style.Font.Bold = true;
            worksheet.Cells[summaryRow, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
            worksheet.Cells[summaryRow, 2].Style.Fill.BackgroundColor.SetColor(ColorTranslator.FromHtml("#00fe00"));
            // Stil ayarlamaları (özet tablo)
            for (int i = summaryRow - 10; i <= summaryRow; i++)
            {
                worksheet.Cells[i, 1, i, 2].Style.Border.Top.Style =
                worksheet.Cells[i, 1, i, 2].Style.Border.Bottom.Style =
                worksheet.Cells[i, 1, i, 2].Style.Border.Left.Style =
                worksheet.Cells[i, 1, i, 2].Style.Border.Right.Style = ExcelBorderStyle.Thin;
            }

            // Rapor oluşturan bilgisini ekle
            summaryRow += 3;
            worksheet.Cells[summaryRow, 1].Value = $"Rapor oluşturma tarihi: {DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")}";
            worksheet.Cells[summaryRow, 1, summaryRow, 4].Merge = true;

            summaryRow++;
            worksheet.Cells[summaryRow, 1].Value = $"Raporu oluşturan: {System.Environment.UserName}";
            worksheet.Cells[summaryRow, 1, summaryRow, 4].Merge = true;

            // Excel dosyasını oluştur ve indir
            return File(
                package.GetAsByteArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Hareketler_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
            );
        }
    }

    [HttpGet]
    public ActionResult DevamsizRapor(DateTime? startDate, DateTime? endDate)
    {
        if (!startDate.HasValue)
        {
            startDate = DateTime.Today;
        }

        if (!endDate.HasValue)
        {
            endDate = DateTime.Today;
        }

        var devamsizPersoneller = new List<DevamsizViewModel>();

        for (DateTime date = startDate.Value; date <= endDate.Value; date = date.AddDays(1))
        {
            var dailyDevamsizlar = db.personel
                .Where(p => !db.Hareketler.Any(h => h.KartNumarasi == p.kartno && h.Tarih == date))
                .Select(p => new DevamsizViewModel
                {
                    PersonelId = p.id,
                    Adi = p.adi,
                    Soyadi = p.soyadi,
                    KartNo = p.kartno,
                    Departman = p.departman,
                    Tarih = date,
                    Bilgi = "Devamsız"
                }).ToList();

            devamsizPersoneller.AddRange(dailyDevamsizlar);
        }

        ViewBag.StartDate = startDate;
        ViewBag.EndDate = endDate;

        return View(devamsizPersoneller);
    }

    [HttpGet]
    public ActionResult ExportDevamsizToExcel(DateTime? startDate, DateTime? endDate, string searchTarih, string searchKartNo, string searchAdi, string searchSoyadi, string searchDepartman, string searchBilgi)
    {
        if (!startDate.HasValue)
        {
            startDate = DateTime.Today;
        }

        if (!endDate.HasValue)
        {
            endDate = DateTime.Today;
        }

        var devamsizPersoneller = new List<DevamsizViewModel>();

        for (DateTime date = startDate.Value; date <= endDate.Value; date = date.AddDays(1))
        {
            var dailyDevamsizlar = db.personel
                .Where(p => !db.Hareketler.Any(h => h.KartNumarasi == p.kartno && h.Tarih == date))
                .Select(p => new DevamsizViewModel
                {
                    Tarih = date,
                    Adi = p.adi,
                    Soyadi = p.soyadi,
                    KartNo = p.kartno,
                    Departman = p.departman,
                    Bilgi = "Devamsız"
                }).ToList();

            devamsizPersoneller.AddRange(dailyDevamsizlar);
        }

        // Apply search filters
        if (!string.IsNullOrEmpty(searchTarih))
        {
            devamsizPersoneller = devamsizPersoneller.Where(p => p.Tarih.ToString("dd.MM.yyyy").Contains(searchTarih)).ToList();
        }
        if (!string.IsNullOrEmpty(searchKartNo))
        {
            devamsizPersoneller = devamsizPersoneller.Where(p => p.KartNo.Contains(searchKartNo)).ToList();
        }
        if (!string.IsNullOrEmpty(searchAdi))
        {
            devamsizPersoneller = devamsizPersoneller.Where(p => p.Adi.Contains(searchAdi)).ToList();
        }
        if (!string.IsNullOrEmpty(searchSoyadi))
        {
            devamsizPersoneller = devamsizPersoneller.Where(p => p.Soyadi.Contains(searchSoyadi)).ToList();
        }
        if (!string.IsNullOrEmpty(searchDepartman))
        {
            devamsizPersoneller = devamsizPersoneller.Where(p => p.Departman.Contains(searchDepartman)).ToList();
        }
        if (!string.IsNullOrEmpty(searchBilgi))
        {
            devamsizPersoneller = devamsizPersoneller.Where(p => p.Bilgi.Contains(searchBilgi)).ToList();
        }

        using (var package = new ExcelPackage())
        {
            var worksheet = package.Workbook.Worksheets.Add("Devamsız Raporu");
            worksheet.Cells["A1"].Value = "Tarih";
            worksheet.Cells["B1"].Value = "Gün";
            worksheet.Cells["C1"].Value = "Adı";
            worksheet.Cells["D1"].Value = "Soyadı";
            worksheet.Cells["E1"].Value = "Kart No";
            worksheet.Cells["F1"].Value = "Departman";
            worksheet.Cells["G1"].Value = "Bilgi";

            for (int i = 0; i < devamsizPersoneller.Count; i++)
            {
                var devamsiz = devamsizPersoneller[i];
                worksheet.Cells[i + 2, 1].Value = devamsiz.Tarih.ToString("dd.MM.yyyy");
                worksheet.Cells[i + 2, 2].Value = devamsiz.Tarih.ToString("dddd");
                worksheet.Cells[i + 2, 3].Value = devamsiz.Adi;
                worksheet.Cells[i + 2, 4].Value = devamsiz.Soyadi;
                worksheet.Cells[i + 2, 5].Value = devamsiz.KartNo;
                worksheet.Cells[i + 2, 6].Value = devamsiz.Departman;
                worksheet.Cells[i + 2, 7].Value = devamsiz.Bilgi;
            }

            // Set column widths
            worksheet.Column(1).Width = 15;  // Tarih
            worksheet.Column(2).Width = 15;  // Gün
            worksheet.Column(3).Width = 20;  // Adı
            worksheet.Column(4).Width = 20;  // Soyadı
            worksheet.Column(5).Width = 15;  // Kart No
            worksheet.Column(6).Width = 20;  // Departman
            worksheet.Column(7).Width = 20;  // Bilgi

            var stream = new MemoryStream();
            package.SaveAs(stream);
            stream.Position = 0;

            string excelName = $"DevamsizRaporu-{DateTime.Now:ddMMyyyyy}.xlsx";
            return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", excelName);
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



    public ActionResult ExportToPdf2(DateTime? StartDate, DateTime? EndDate, int? SelectedPersonelId)
    {
        DateTime startDate = StartDate ?? DateTime.Now.Date;
        DateTime endDate = EndDate ?? DateTime.Now.Date;
        endDate = endDate.AddHours(23).AddMinutes(59).AddSeconds(59).AddMilliseconds(999);

        var personelListesi = GetPersonelListesi();
        var hareketListesi = GetHareketListesi();
        var vardiyaListesi = GetVardiyaListesi();
        var izinListesi = GetIzinListesi();

        var selectedPersonel = SelectedPersonelId.HasValue ? personelListesi.FirstOrDefault(p => p.id == SelectedPersonelId.Value) : null;
        if (selectedPersonel != null)
        {
            hareketListesi = hareketListesi.Where(h => h.kartno == selectedPersonel.kartno).ToList();
            personelListesi = personelListesi.Where(p => p.id == SelectedPersonelId.Value).ToList();
            // İzin listesini de filtreleyerek seçilen personelin izinlerini alalım
            izinListesi = izinListesi.Where(i => i.Personelıd == selectedPersonel.id).ToList();
        }

        int daysInMonth = DateTime.DaysInMonth(startDate.Year, startDate.Month);
        var allDates = Enumerable.Range(0, daysInMonth)
                                 .Select(offset => new DateTime(startDate.Year, startDate.Month, 1).AddDays(offset))
                                 .ToList();

        // İzin günlerini takip etmek için değişkenler
        int toplamUcretliIzinGunSayisi = 0;
        int toplamUcretsizIzinGunSayisi = 0;

        var hareketData = from tarih in allDates
                          from personel in personelListesi
                          let hareketler = hareketListesi.Where(h => h.Tarih.Date == tarih.Date && h.kartno == personel.kartno).ToList()
                          // Bu günün izin durumunu kontrol et
                          let izin = izinListesi.FirstOrDefault(i => i.BaslangicTarihi.Date <= tarih.Date && i.BitisTarihi.Date >= tarih.Date)
                          select new
                          {
                              Tarih = tarih,
                              PersonelId = personel.id,
                              personelAdi = personel.adi,
                              personelSoyadi = personel.soyadi,
                              kartno = personel.kartno,
                              GirişSaatleri = hareketler.Where(h => h.IslemTipi == "01").OrderBy(h => h.Saat).Select(h => h.Saat.ToString(@"hh\:mm")).ToList(),
                              ÇıkışSaatleri = hareketler.Where(h => h.IslemTipi == "02").OrderByDescending(h => h.Saat).Select(h => h.Saat.ToString(@"hh\:mm")).ToList(),
                              Izin = izin,
                              // İzin varsa izin türünü, yoksa normal durumu göster
                              Bilgi = izin != null ? izin.IzinTuru :
                                      !hareketler.Any() ? "DEVAMSIZ" :
                                      hareketler.Any(h => h.IslemTipi == "01") && hareketler.Any(h => h.IslemTipi == "02") ? "OK" :
                                      hareketler.Any(h => h.IslemTipi == "01") ? "ÇIKIŞ YOK" : "GİRİŞ YOK",
                              Vardiya = vardiyaListesi.FirstOrDefault(v => v.VardiyaId == personel.VardiyaId)
                          };

        List<TimeSpan> calismaSuresiListesi = new List<TimeSpan>();
        List<TimeSpan> fazlaMesai1Listesi = new List<TimeSpan>();
        List<TimeSpan> fazlaMesai3Listesi = new List<TimeSpan>();
        List<TimeSpan> eksikCalismaListesi = new List<TimeSpan>();
        int toplamDevamsizlikGunSayisi = 0;

        foreach (var data in hareketData)
        {
            // İzin durumunu kontrol et
            if (data.Izin != null)
            {
                // İzin türü kontrolü
                if (data.Izin.IzinTuru == "ÜCRETSİZ")
                {
                    toplamUcretsizIzinGunSayisi++;
                }
                else
                {
                    // Diğer tüm izin türleri ücretli kabul edilsin
                    toplamUcretliIzinGunSayisi++;
                }

                // İzin günlerinde devamsız ve eksik çalışma hesaplanmasın
                continue;
            }

            // İzin olmayan günler için normal hesaplama
            if (data.Bilgi == "DEVAMSIZ")
            {
                toplamDevamsizlikGunSayisi++;
                // Eksik çalışma süresine vardiya süresi ekle
                if (data.Vardiya != null)
                {
                    var vardiyaSuresi = data.Vardiya.CalismaBitis - data.Vardiya.CalismaBaslangic;
                    if (vardiyaSuresi < TimeSpan.Zero)
                    {
                        vardiyaSuresi = vardiyaSuresi.Add(TimeSpan.FromHours(24));
                    }
                    eksikCalismaListesi.Add(vardiyaSuresi);
                }
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

                double calismaSaat = calismaSuresiTimeSpan.TotalHours;
                var vardiya = data.Vardiya;
                double vardiyaSaat = 9.30; // Varsayılan vardiya süresi 9.30 saat
                if (vardiya != null)
                {
                    vardiyaSaat = (vardiya.CalismaBitis - vardiya.CalismaBaslangic).TotalHours;
                }

                // Serbest çalışma kontrolü
                if (vardiya != null && (vardiya.serbestcalisma ?? false))
                {
                    // Serbest çalışma durumunda hesaplama
                    if (calismaSaat > vardiyaSaat)
                    {
                        double fazlaMesaiSaat = calismaSaat - vardiyaSaat;
                        fazlaMesai1Listesi.Add(TimeSpan.FromHours(fazlaMesaiSaat));
                    }

                    // Eksik çalışma süresi hesapla
                    if (calismaSaat < vardiyaSaat)
                    {
                        double eksikCalismaSaat = vardiyaSaat - calismaSaat;
                        eksikCalismaListesi.Add(TimeSpan.FromHours(eksikCalismaSaat));
                    }
                }
                else if (vardiya != null)
                {
                    // Normal vardiya hesaplama
                    var vardiyaBaslangic = vardiya.CalismaBaslangic;
                    var vardiyaBitis = vardiya.CalismaBitis;

                    // Fazla mesai başlangıcı hesaplama
                    TimeSpan fazlaMesaiBaslangic = vardiya.MesaiBaslangic != TimeSpan.Zero ?
                        vardiya.MesaiBaslangic : vardiyaBitis;

                    // Başlangıçta fazla mesai değerlerini sıfırlayalım
                    TimeSpan fazlaMesai1 = TimeSpan.Zero; // Çıkış sonrası fazla mesai
                    TimeSpan fazlaMesai3 = TimeSpan.Zero; // Erken mesai (giriş öncesi)

                    // Eğer vardiya başlangıcından önce geldiyse, vardiya başlangıcını kullan
                    TimeSpan efektifGirisSaati = girisSaat.TimeOfDay < vardiyaBaslangic ? vardiyaBaslangic : girisSaat.TimeOfDay;

                    // Eğer vardiya bitişinden sonra çıktıysa, vardiya bitişini kullan
                    TimeSpan efektifCikisSaati = cikisSaat.TimeOfDay > vardiyaBitis ? vardiyaBitis : cikisSaat.TimeOfDay;

                    // Çalışma süresini efektif giriş ve çıkış saatleri ile hesapla
                    var calismaSuresi = efektifCikisSaati - efektifGirisSaati;
                    if (calismaSuresi < TimeSpan.Zero)
                    {
                        calismaSuresi = calismaSuresi.Add(TimeSpan.FromHours(24));
                    }
                    calismaSuresiListesi.Add(calismaSuresi);

                    // Eksik çalışma süresini hesaplama - vardiya süresi ile çalışma süresi arasındaki fark
                    if (vardiyaSaat > calismaSuresi.TotalHours)
                    {
                        // Eksik süre = Vardiya süresi - Çalışma süresi
                        var eksikCalismaSaat = vardiyaSaat - calismaSuresi.TotalHours;
                        eksikCalismaListesi.Add(TimeSpan.FromHours(eksikCalismaSaat));
                    }

                    // Çıkış sonrası fazla mesai hesaplama (fazlaMesai1)
                    if (cikisSaat.TimeOfDay > fazlaMesaiBaslangic)
                    {
                        TimeSpan gecenSure = cikisSaat.TimeOfDay - fazlaMesaiBaslangic;
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

                    fazlaMesai1Listesi.Add(fazlaMesai1);

                    // ErkenMesai özelliği etkinse ve personel erken geldiyse
                    if (vardiya.ErkenMesai == true && girisSaat.TimeOfDay < vardiyaBaslangic)
                    {
                        // Erken gelme süresini hesapla (vardiya başlangıcından ne kadar önce geldi)
                        TimeSpan erkenGelmeSuresi = vardiyaBaslangic - girisSaat.TimeOfDay;
                        if (erkenGelmeSuresi < TimeSpan.Zero)
                        {
                            erkenGelmeSuresi = erkenGelmeSuresi.Add(TimeSpan.FromHours(24));
                        }

                        // Erken gelme süresini FazlaMesai3'e ekle
                        fazlaMesai3 = erkenGelmeSuresi;
                        fazlaMesai3Listesi.Add(fazlaMesai3);
                    }
                }
            }
        }

        TimeSpan toplamCalismaSuresi = new TimeSpan(calismaSuresiListesi.Sum(ts => ts.Ticks));
        TimeSpan toplamFazlaMesai1 = new TimeSpan(fazlaMesai1Listesi.Sum(ts => ts.Ticks));
        TimeSpan toplamFazlaMesai3 = new TimeSpan(fazlaMesai3Listesi.Sum(ts => ts.Ticks));
        TimeSpan toplamEksikCalismaSuresi = new TimeSpan(eksikCalismaListesi.Sum(ts => ts.Ticks));

        using (var stream = new MemoryStream())
        {
            // A4 boyutunda PDF dokümanı oluştur
            var pdfDoc = new PdfSharp.Pdf.PdfDocument();
            pdfDoc.PageLayout = PdfSharp.Pdf.PdfPageLayout.SinglePage;

            // Standart A4 sayfa boyutu (595x842 points - 210x297 mm)
            var page = pdfDoc.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            // Dikey yönlendirme (portrait)
            page.Orientation = PdfSharp.PageOrientation.Portrait;

            var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Verdana", 8); // Daha küçük font kullanarak daha fazla içeriğe yer açıyoruz
            var boldFont = new XFont("Verdana", 8);
            var headerBrush = new XSolidBrush(XColors.LightGray);
            var borderPen = new XPen(XColors.Black, 0.5); // İnce kenarlık
            var tableHeaderBrush = new XSolidBrush(XColors.Gray);
            var tableRowBrush = new XSolidBrush(XColors.LightGray);

            // Kenar boşlukları
            double margin = 20;
            double yPoint = 40;
            double tableWidth = page.Width - 2 * margin; // Sayfa genişliğinden kenar boşluklarını çıkarıyoruz

            // Başlık
            gfx.DrawString("Aylık Personel İcmal Raporu", new XFont("Verdana", 12), XBrushes.Black,
                new XRect(margin, yPoint, tableWidth, 20), XStringFormats.TopLeft);
            yPoint += 20;

            // Tarih aralığı
            gfx.DrawString($"{startDate:MMMM yyyy}", font, XBrushes.Black,
                new XRect(margin, yPoint, tableWidth, 20), XStringFormats.TopRight);
            yPoint += 20;

            // Kişisel Bilgiler Tablosu - A4 sayfaya sığacak şekilde ayarlama
            double[] personalInfoColumnWidths = { tableWidth * 0.25, tableWidth * 0.25, tableWidth * 0.25, tableWidth * 0.25 };
            string[] personalInfoHeaders = { "ADI", "SOYADI", "DEPARTMAN", "İŞE GİRİŞ TARİHİ" };
            string[] personalInfoValues = {
            selectedPersonel?.adi ?? "Bilinmiyor",
            selectedPersonel?.soyadi ?? "Bilinmiyor",
            selectedPersonel?.departman ?? "Bilinmiyor",
            selectedPersonel?.isegiristarih?.ToString("dd.MM.yyyy") ?? "Bilinmiyor"
        };

            // Başlık satırı
            double xPosition = margin;
            for (int i = 0; i < personalInfoHeaders.Length; i++)
            {
                gfx.DrawRectangle(tableHeaderBrush, new XRect(xPosition, yPoint, personalInfoColumnWidths[i], 20));
                gfx.DrawRectangle(borderPen, new XRect(xPosition, yPoint, personalInfoColumnWidths[i], 20));
                gfx.DrawString(personalInfoHeaders[i], boldFont, XBrushes.White,
                    new XRect(xPosition + 2, yPoint, personalInfoColumnWidths[i] - 4, 20), XStringFormats.Center);
                xPosition += personalInfoColumnWidths[i];
            }

            yPoint += 20;

            // Değer satırı
            xPosition = margin;
            for (int i = 0; i < personalInfoValues.Length; i++)
            {
                gfx.DrawRectangle(tableRowBrush, new XRect(xPosition, yPoint, personalInfoColumnWidths[i], 20));
                gfx.DrawRectangle(borderPen, new XRect(xPosition, yPoint, personalInfoColumnWidths[i], 20));
                gfx.DrawString(personalInfoValues[i], font, XBrushes.Black,
                    new XRect(xPosition + 2, yPoint, personalInfoColumnWidths[i] - 4, 20), XStringFormats.Center);
                xPosition += personalInfoColumnWidths[i];
            }

            yPoint += 25;

            // Kişisel Bilgiler Tablosu - Saat Ücreti ve Net Maaş
            double[] additionalInfoColumnWidths = { tableWidth * 0.5, tableWidth * 0.5 };
            string[] additionalInfoHeaders = { "SAAT ÜCRETİ", "NET MAAŞ" };
            string[] additionalInfoValues = { "0", "0" };

            // Başlık satırı
            xPosition = margin;
            for (int i = 0; i < additionalInfoHeaders.Length; i++)
            {
                gfx.DrawRectangle(tableHeaderBrush, new XRect(xPosition, yPoint, additionalInfoColumnWidths[i], 20));
                gfx.DrawRectangle(borderPen, new XRect(xPosition, yPoint, additionalInfoColumnWidths[i], 20));
                gfx.DrawString(additionalInfoHeaders[i], boldFont, XBrushes.White,
                    new XRect(xPosition + 2, yPoint, additionalInfoColumnWidths[i] - 4, 20), XStringFormats.Center);
                xPosition += additionalInfoColumnWidths[i];
            }

            yPoint += 20;

            // Değer satırı
            xPosition = margin;
            for (int i = 0; i < additionalInfoValues.Length; i++)
            {
                gfx.DrawRectangle(tableRowBrush, new XRect(xPosition, yPoint, additionalInfoColumnWidths[i], 20));
                gfx.DrawRectangle(borderPen, new XRect(xPosition, yPoint, additionalInfoColumnWidths[i], 20));
                gfx.DrawString(additionalInfoValues[i], font, XBrushes.Black,
                    new XRect(xPosition + 2, yPoint, additionalInfoColumnWidths[i] - 4, 20), XStringFormats.Center);
                xPosition += additionalInfoColumnWidths[i];
            }

            yPoint += 25;

            // Toplam Bilgiler Tablosu - Kazançlar ve Kesintiler - İzin değerlerini ekledik
            double[] totalInfoColumnWidths = { tableWidth * 0.5, tableWidth * 0.5 };
            string[] totalInfoHeaders = { "KAZANÇLAR", "KESİNTİLER" };
            // İstediğiniz gibi ÜCRETLİ İZİN ve ÜCRETSİZ İZİN değerlerini ekledik
            string[] kazancValues = {
            $"Çalışma Süresi: {Math.Floor(toplamCalismaSuresi.TotalHours):00}:{toplamCalismaSuresi.Minutes:00}",
            $"FM1: {Math.Floor(toplamFazlaMesai1.TotalHours):00}:{toplamFazlaMesai1.Minutes:00}",
            $"FM3: {Math.Floor(toplamFazlaMesai3.TotalHours):00}:{toplamFazlaMesai3.Minutes:00}",
            $"Ücretli İzin: {toplamUcretliIzinGunSayisi} gün"
        };
            string[] kesintiValues = {
            $"Devamsızlık: {toplamDevamsizlikGunSayisi} gün",
            $"Eksik Çalışma: {Math.Floor(toplamEksikCalismaSuresi.TotalHours):00}:{toplamEksikCalismaSuresi.Minutes:00}",
            $"Ücretsiz İzin: {toplamUcretsizIzinGunSayisi} gün"
        };

            // Başlık satırı
            xPosition = margin;
            for (int i = 0; i < totalInfoHeaders.Length; i++)
            {
                gfx.DrawRectangle(tableHeaderBrush, new XRect(xPosition, yPoint, totalInfoColumnWidths[i], 20));
                gfx.DrawRectangle(borderPen, new XRect(xPosition, yPoint, totalInfoColumnWidths[i], 20));
                gfx.DrawString(totalInfoHeaders[i], boldFont, XBrushes.White,
                    new XRect(xPosition + 2, yPoint, totalInfoColumnWidths[i] - 4, 20), XStringFormats.Center);
                xPosition += totalInfoColumnWidths[i];
            }

            yPoint += 20;

            // Değer satırları
            int maxRows = Math.Max(kazancValues.Length, kesintiValues.Length);
            for (int i = 0; i < maxRows; i++)
            {
                xPosition = margin;

                // Kazanç değeri
                gfx.DrawRectangle(tableRowBrush, new XRect(xPosition, yPoint, totalInfoColumnWidths[0], 20));
                gfx.DrawRectangle(borderPen, new XRect(xPosition, yPoint, totalInfoColumnWidths[0], 20));
                if (i < kazancValues.Length)
                {
                    gfx.DrawString(kazancValues[i], font, XBrushes.Black,
                        new XRect(xPosition + 10, yPoint, totalInfoColumnWidths[0] - 20, 20), XStringFormats.CenterLeft);
                }
                xPosition += totalInfoColumnWidths[0];

                // Kesinti değeri
                gfx.DrawRectangle(tableRowBrush, new XRect(xPosition, yPoint, totalInfoColumnWidths[1], 20));
                gfx.DrawRectangle(borderPen, new XRect(xPosition, yPoint, totalInfoColumnWidths[1], 20));
                if (i < kesintiValues.Length)
                {
                    gfx.DrawString(kesintiValues[i], font, XBrushes.Black,
                        new XRect(xPosition + 10, yPoint, totalInfoColumnWidths[1] - 20, 20), XStringFormats.CenterLeft);
                }

                yPoint += 20;
            }

            yPoint += 25;

            // Hareket Tablosu - A4 sayfaya uygun genişlikte sütun düzeni
            double[] columnWidths = {
            tableWidth * 0.14,  // Tarih
            tableWidth * 0.10,  // Kart No
            tableWidth * 0.15,  // Ad
            tableWidth * 0.15,  // Soyad
            tableWidth * 0.15,  // Giriş Saatleri
            tableWidth * 0.15,  // Çıkış Saatleri
            tableWidth * 0.16   // Bilgi
        };
            string[] headers = { "TARİH", "KART NO", "AD", "SOYAD", "GİRİŞ", "ÇIKIŞ", "BİLGİ" };

            // Başlık satırı
            xPosition = margin;
            for (int i = 0; i < headers.Length; i++)
            {
                gfx.DrawRectangle(tableHeaderBrush, new XRect(xPosition, yPoint, columnWidths[i], 18)); // Yükseklik biraz azaltıldı
                gfx.DrawRectangle(borderPen, new XRect(xPosition, yPoint, columnWidths[i], 18));
                gfx.DrawString(headers[i], boldFont, XBrushes.White,
                    new XRect(xPosition + 2, yPoint, columnWidths[i] - 4, 18), XStringFormats.Center);
                xPosition += columnWidths[i];
            }

            yPoint += 18;

            // Veri satırları
            bool isGrayRow = false;
            foreach (var data in hareketData)
            {
                // Sayfa sınırını kontrol et ve gerekirse yeni sayfa ekle
                if (yPoint + 18 > page.Height - margin)
                {
                    page = pdfDoc.AddPage();
                    page.Size = PdfSharp.PageSize.A4;
                    page.Orientation = PdfSharp.PageOrientation.Portrait;
                    gfx = XGraphics.FromPdfPage(page);
                    yPoint = margin;

                    // Yeni sayfada başlık satırını tekrar ekle
                    xPosition = margin;
                    for (int i = 0; i < headers.Length; i++)
                    {
                        gfx.DrawRectangle(tableHeaderBrush, new XRect(xPosition, yPoint, columnWidths[i], 18));
                        gfx.DrawRectangle(borderPen, new XRect(xPosition, yPoint, columnWidths[i], 18));
                        gfx.DrawString(headers[i], boldFont, XBrushes.White,
                            new XRect(xPosition + 2, yPoint, columnWidths[i] - 4, 18), XStringFormats.Center);
                        xPosition += columnWidths[i];
                    }
                    yPoint += 18;
                }

                xPosition = margin;
                XBrush rowBrush = isGrayRow ? tableRowBrush : XBrushes.White;
                var rowHeight = 18; // Satır yüksekliği

                // Tarih sütunu
                gfx.DrawRectangle(rowBrush, new XRect(xPosition, yPoint, columnWidths[0], rowHeight));
                gfx.DrawRectangle(borderPen, new XRect(xPosition, yPoint, columnWidths[0], rowHeight));
                gfx.DrawString(data.Tarih.ToString("dd-MM-yyyy"), font, XBrushes.Black,
                    new XRect(xPosition + 2, yPoint, columnWidths[0] - 4, rowHeight), XStringFormats.CenterLeft);
                xPosition += columnWidths[0];

                // Kartno sütunu
                gfx.DrawRectangle(rowBrush, new XRect(xPosition, yPoint, columnWidths[1], rowHeight));
                gfx.DrawRectangle(borderPen, new XRect(xPosition, yPoint, columnWidths[1], rowHeight));
                gfx.DrawString(data.kartno, font, XBrushes.Black,
                    new XRect(xPosition + 2, yPoint, columnWidths[1] - 4, rowHeight), XStringFormats.Center);
                xPosition += columnWidths[1];

                // Ad sütunu
                gfx.DrawRectangle(rowBrush, new XRect(xPosition, yPoint, columnWidths[2], rowHeight));
                gfx.DrawRectangle(borderPen, new XRect(xPosition, yPoint, columnWidths[2], rowHeight));
                gfx.DrawString(data.personelAdi ?? "Bilinmiyor", font, XBrushes.Black,
                    new XRect(xPosition + 2, yPoint, columnWidths[2] - 4, rowHeight), XStringFormats.Center);
                xPosition += columnWidths[2];

                // Soyad sütunu
                gfx.DrawRectangle(rowBrush, new XRect(xPosition, yPoint, columnWidths[3], rowHeight));
                gfx.DrawRectangle(borderPen, new XRect(xPosition, yPoint, columnWidths[3], rowHeight));
                gfx.DrawString(data.personelSoyadi ?? "Bilinmiyor", font, XBrushes.Black,
                    new XRect(xPosition + 2, yPoint, columnWidths[3] - 4, rowHeight), XStringFormats.Center);
                xPosition += columnWidths[3];

                // Giriş Saatleri sütunu - metin uzunluğu kontrolü ve küçültme
                string girisSaatleri = data.GirişSaatleri.Any()
                    ? string.Join(", ", data.GirişSaatleri)
                    : "Giriş Yok";

                gfx.DrawRectangle(rowBrush, new XRect(xPosition, yPoint, columnWidths[4], rowHeight));
                gfx.DrawRectangle(borderPen, new XRect(xPosition, yPoint, columnWidths[4], rowHeight));

                // A4 sayfaya sığması için metin uzunluğu kontrolü
                if (gfx.MeasureString(girisSaatleri, font).Width > columnWidths[4] - 6)
                {
                    var smallerFont = new XFont(font.Name, font.Size - 1);
                    gfx.DrawString(girisSaatleri, smallerFont, XBrushes.Black,
                        new XRect(xPosition + 2, yPoint, columnWidths[4] - 4, rowHeight), XStringFormats.Center);
                }
                else
                {
                    gfx.DrawString(girisSaatleri, font, XBrushes.Black,
        new XRect(xPosition + 2, yPoint, columnWidths[4] - 4, rowHeight), XStringFormats.Center);
                }
                xPosition += columnWidths[4];

                // Çıkış Saatleri sütunu - metin uzunluğu kontrolü ve küçültme
                string cikisSaatleri = data.ÇıkışSaatleri.Any()
                    ? string.Join(", ", data.ÇıkışSaatleri)
                    : "Çıkış Yok";

                gfx.DrawRectangle(rowBrush, new XRect(xPosition, yPoint, columnWidths[5], rowHeight));
                gfx.DrawRectangle(borderPen, new XRect(xPosition, yPoint, columnWidths[5], rowHeight));

                // A4 sayfaya sığması için metin uzunluğu kontrolü
                if (gfx.MeasureString(cikisSaatleri, font).Width > columnWidths[5] - 6)
                {
                    var smallerFont = new XFont(font.Name, font.Size - 1);
                    gfx.DrawString(cikisSaatleri, smallerFont, XBrushes.Black,
                        new XRect(xPosition + 2, yPoint, columnWidths[5] - 4, rowHeight), XStringFormats.Center);
                }
                else
                {
                    gfx.DrawString(cikisSaatleri, font, XBrushes.Black,
                        new XRect(xPosition + 2, yPoint, columnWidths[5] - 4, rowHeight), XStringFormats.Center);
                }
                xPosition += columnWidths[5];

                // Bilgi sütunu - İzin türünü buraya ekledik
                XBrush bilgiColor = XBrushes.Black;

                // İzin türüne göre renklendirme (isteğe bağlı)
                if (data.Izin != null)
                {
                    // İzin bilgisini öne çıkar
                    bilgiColor = data.Izin.IzinTuru == "ÜCRETSİZ İZİN" ? XBrushes.Red : XBrushes.Blue;
                }

                gfx.DrawRectangle(rowBrush, new XRect(xPosition, yPoint, columnWidths[6], rowHeight));
                gfx.DrawRectangle(borderPen, new XRect(xPosition, yPoint, columnWidths[6], rowHeight));
                gfx.DrawString(data.Bilgi, font, bilgiColor,
                    new XRect(xPosition + 2, yPoint, columnWidths[6] - 4, rowHeight), XStringFormats.Center);

                yPoint += rowHeight;
                isGrayRow = !isGrayRow;
            }

        

            // Sayfa altı bilgileri
            yPoint = page.Height - 40;
            gfx.DrawString("© " + DateTime.Now.Year.ToString() + " - APDKS", new XFont("Verdana", 7), XBrushes.Gray,
                new XRect(margin, yPoint, tableWidth, 20), XStringFormats.Center);

            pdfDoc.Save(stream, false);
            return File(stream.ToArray(), "application/pdf", "PersonelAylikIcmalRaporu.pdf");
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
    }
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