// Trang tin là site HTML tĩnh — file này là JS duy nhất, xử lý 2 việc CẦN động:
// tìm kiếm (trang /tim-kiem/) và đăng ký nhận tin (form ở trang chủ).
//
// Cả 2 đều gọi qua "proxy.php" đặt CÙNG thư mục với site tĩnh này (vd public_html/news/),
// KHÔNG gọi thẳng sang domain backend — browser gọi cùng-origin nên không dính CORS; proxy.php
// mới là bên gọi chéo domain, và đó là việc của PHP chạy trên server, không phải trình duyệt.
(function () {
  "use strict";

  var basePath = window.NEWS_BASE_PATH || "";
  var proxyUrl = basePath + "/api-proxy.php";

  function esc(s) {
    var div = document.createElement("div");
    div.textContent = s || "";
    return div.innerHTML;
  }

  // ── Tìm kiếm (trang /tim-kiem/) ──────────────────────────────────────────
  var resultsEl = document.getElementById("search-results");
  var statusEl = document.getElementById("search-status");

  if (resultsEl && statusEl) {
    var params = new URLSearchParams(window.location.search);
    var q = (params.get("q") || "").trim();

    if (q.length < 2) {
      statusEl.textContent = "Nhập ít nhất 2 ký tự để tìm.";
    } else {
      statusEl.textContent = "Đang tìm “" + q + "”…";
      fetch(proxyUrl + "?path=search&q=" + encodeURIComponent(q))
        .then(function (res) { return res.json(); })
        .then(function (json) {
          var items = (json && json.data) || [];
          if (items.length === 0) {
            statusEl.textContent = "Không tìm thấy bài nào khớp “" + q + "”.";
            return;
          }
          statusEl.textContent = items.length + " kết quả cho “" + q + "”:";
          resultsEl.innerHTML = items.map(function (a) {
            var href = basePath + "/tin/" + a.slug + ".html";
            var img = a.imageUrl
              ? '<img src="' + esc(a.imageUrl) + '" alt="" loading="lazy" decoding="async" onerror="this.remove()">'
              : "";
            return (
              '<article class="card">' +
              '<a href="' + href + '" class="thumb" aria-hidden="true" tabindex="-1">' + img + "</a>" +
              '<div class="card-body">' +
              '<h3><a href="' + href + '">' + esc(a.title) + "</a></h3>" +
              (a.sapo ? "<p>" + esc(a.sapo) + "</p>" : "") +
              "</div></article>"
            );
          }).join("");
        })
        .catch(function () {
          statusEl.textContent = "Không tìm được lúc này — thử lại sau.";
        });
    }
  }

  // ── Đăng ký nhận tin (trang chủ) ─────────────────────────────────────────
  var form = document.getElementById("newsletter-form");
  var msgEl = document.getElementById("newsletter-msg");

  if (form && msgEl) {
    form.addEventListener("submit", function (ev) {
      ev.preventDefault();
      var email = (form.querySelector("#email") || {}).value || "";
      email = email.trim();
      if (!email || email.indexOf("@") === -1) {
        msgEl.textContent = "Nhập email hợp lệ.";
        return;
      }

      var submitBtn = form.querySelector("button[type=submit]");
      if (submitBtn) submitBtn.disabled = true;
      msgEl.textContent = "Đang đăng ký…";

      fetch(proxyUrl + "?path=subscribe", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email: email }),
      })
        .then(function (res) { return res.json(); })
        .then(function (json) {
          if (json && json.success) {
            msgEl.textContent = "Đã đăng ký thành công!";
            form.reset();
          } else {
            msgEl.textContent = (json && json.message) || "Đăng ký không thành công — thử lại sau.";
          }
        })
        .catch(function () {
          msgEl.textContent = "Đăng ký không thành công — thử lại sau.";
        })
        .finally(function () {
          if (submitBtn) submitBtn.disabled = false;
        });
    });
  }
})();
