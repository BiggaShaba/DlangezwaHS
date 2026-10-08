// ────────────────────────────────────────────────────────────────────────────
// Dlangezwa HS – site.js
// ────────────────────────────────────────────────────────────────────────────

// Load available beds when room selection changes (admin allocation page)
document.addEventListener('DOMContentLoaded', function () {
  const roomSelect = document.getElementById('RoomId');
  const bedSelect  = document.getElementById('BedId');

  if (roomSelect && bedSelect) {
    roomSelect.addEventListener('change', async function () {
      const roomId = this.value;
      bedSelect.innerHTML = '<option value="">Loading…</option>';
      if (!roomId) { bedSelect.innerHTML = '<option value="">— Select Room first —</option>'; return; }

      try {
        const resp = await fetch(`/api/rooms/${roomId}/beds`);
        const beds = await resp.json();
        bedSelect.innerHTML = beds.length
          ? beds.map(b => `<option value="${b.id}">${b.bedNumber}</option>`).join('')
          : '<option value="">No beds available</option>';
      } catch {
        bedSelect.innerHTML = '<option value="">Error loading beds</option>';
      }
    });
  }

  // Subject checkboxes – require at least one
  const enrollForm = document.getElementById('enroll-form');
  if (enrollForm) {
    enrollForm.addEventListener('submit', function (e) {
      const checked = this.querySelectorAll('input[name="SubjectIds"]:checked');
      if (checked.length === 0) {
        e.preventDefault();
        const err = document.getElementById('subject-error');
        if (err) err.classList.remove('d-none');
      }
    });
  }

  // File size / type validation on apply form
  const fileInputs = document.querySelectorAll('input[type="file"].dhs-doc');
  fileInputs.forEach(input => {
    input.addEventListener('change', function () {
      const maxMb = 5;
      const allowed = ['application/pdf', 'image/jpeg', 'image/png'];
      const file = this.files[0];
      const feedback = document.getElementById(this.id + '-feedback');
      if (!file) return;
      if (file.size > maxMb * 1024 * 1024) {
        if (feedback) { feedback.textContent = `File too large (max ${maxMb} MB).`; feedback.classList.remove('d-none'); }
        this.value = '';
      } else if (!allowed.includes(file.type)) {
        if (feedback) { feedback.textContent = 'Only PDF, JPG, PNG allowed.'; feedback.classList.remove('d-none'); }
        this.value = '';
      } else {
        if (feedback) feedback.classList.add('d-none');
      }
    });
  });

  // Payment type → amount auto-fill
  const payTypeSelect = document.getElementById('Type');
  const payAmountInput = document.getElementById('Amount');
  if (payTypeSelect && payAmountInput) {
    payTypeSelect.addEventListener('change', function () {
      const fees = window.feeAmounts || {};
      const val = fees[this.value];
      if (val !== undefined) payAmountInput.value = val;
    });
  }

  // Confirm destructive actions
  document.querySelectorAll('[data-confirm]').forEach(el => {
    el.addEventListener('click', function (e) {
      if (!confirm(this.dataset.confirm || 'Are you sure?')) e.preventDefault();
    });
  });

  // Auto-dismiss alerts after 6s
  setTimeout(() => {
    document.querySelectorAll('.alert-dismissible').forEach(a => {
      const bsAlert = bootstrap.Alert.getOrCreateInstance(a);
      bsAlert.close();
    });
  }, 6000);
});
