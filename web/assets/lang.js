// Picks Spanish or English and shows only that language.
// Order: ?lang= in the URL, then the browser language. Nothing is stored — no cookies, no
// localStorage — because this site's own privacy page says it keeps nothing.
(function () {
  var param = new URLSearchParams(location.search).get("lang");
  var browser = (navigator.language || "es").toLowerCase().slice(0, 2);
  var lang = param === "en" || param === "es" ? param : (browser === "es" ? "es" : "en");

  function apply(l) {
    document.documentElement.dataset.lang = l;
    document.documentElement.lang = l;
    document.querySelectorAll(".lang button").forEach(function (b) {
      b.setAttribute("aria-pressed", String(b.dataset.set === l));
    });
    var t = document.querySelector('meta[name="title-' + l + '"]');
    if (t) document.title = t.content;
  }

  apply(lang);

  document.addEventListener("click", function (e) {
    var b = e.target.closest(".lang button");
    if (!b) return;
    apply(b.dataset.set);
    var url = new URL(location.href);
    url.searchParams.set("lang", b.dataset.set);
    history.replaceState(null, "", url);
  });
})();
