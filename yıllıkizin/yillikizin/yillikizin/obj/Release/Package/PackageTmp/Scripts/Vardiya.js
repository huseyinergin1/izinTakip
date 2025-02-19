$(document).ready(function () {
    // Yeni vardiya ekleme butonuna tıklandığında
    $("#addVardiyaBtn").click(function () {
        $.get("/Vardiya/VardiyaForm", function (html) {
            $("#modalBodyContent").html(html);
            $("#vardiyaModalLabel").text("Yeni Vardiya Ekle");
            $("#vardiyaModal").modal("show");
        });
    });

    // Düzenleme butonuna tıklandığında
    $(".edit-vardiya").click(function () {
        const id = $(this).data("id");
        $.get(`/Vardiya/VardiyaForm/${id}`, function (html) {
            $("#modalBodyContent").html(html);
            $("#vardiyaModalLabel").text("Vardiya Düzenle");
            $("#vardiyaModal").modal("show");
        });
    });

    // Form gönderildiğinde
    $(document).on("submit", "#vardiyaForm", function (e) {
        e.preventDefault();

        const form = $(this);
        const actionUrl = form.attr("action");
        const formData = form.serialize();

        $.post(actionUrl, formData, function (response) {
            if (response.success) {
                alert("Vardiya başarıyla kaydedildi.");
                location.reload(); // Sayfayı yeniler
            } else {
                $("#modalBodyContent").html(response); // Hata varsa formu tekrar yükle
            }
        });
    });
});
