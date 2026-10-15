document.addEventListener("DOMContentLoaded", function () {
  var first = document.getElementById("firstName");
  var last = document.getElementById("lastName");
  if (!first || !last) {
    return;
  }

  var hideLast = function () {
    var group = last.closest(".form-group, .pf-v5-c-form__group, .pf-c-form__group, [id^='kc-form']");
    if (group && group !== document.body) {
      group.style.display = "none";
    } else {
      last.style.display = "none";
      var label = document.querySelector("label[for='lastName']");
      if (label) {
        label.style.display = "none";
      }
    }
  };

  var copyName = function () {
    if (!last.value || last.value === "-") {
      last.value = first.value || "-";
    }
  };

  hideLast();
  copyName();
  first.addEventListener("input", function () {
    last.value = first.value || "-";
  });
});
