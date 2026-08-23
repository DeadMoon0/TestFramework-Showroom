// The storefront's entire behaviour, small enough to read in one breath: adding counts the item -
// whether by button or by dragging a product onto the cart - checkout confirms a moment later on the
// cart's data-state, stock tips live only under the pointer, the search filter hides what does not
// match as the user types, and arriving plants one honest cookie.
(() => {
  document.cookie = 'storefront-visited=yes; path=/';

  let items = 0;
  const summary = document.querySelector('[data-testid="cart-summary"]');
  const cart = document.querySelector('section[aria-label="Cart"]');

  const addItem = () => {
    items += 1;
    summary.textContent = items === 1 ? '1 item in cart' : `${items} items in cart`;
  };

  for (const button of document.querySelectorAll('shop-product button')) {
    button.addEventListener('click', addItem);
  }

  // The summary answers immediately; the section's state confirms after the round-trip a real
  // payment would take.
  cart.querySelector('button').addEventListener('click', () => {
    summary.textContent = 'Order placed';
    setTimeout(() => cart.setAttribute('data-state', 'confirmed'), 600);
  });

  for (const product of document.querySelectorAll('shop-product')) {
    const tip = product.querySelector('.stock');
    product.addEventListener('mouseenter', () => { tip.hidden = false; });
    product.addEventListener('mouseleave', () => { tip.hidden = true; });

    product.addEventListener('dragstart', event => {
      event.dataTransfer.setData('text/plain', product.getAttribute('data-sku'));
    });
  }

  cart.addEventListener('dragover', event => event.preventDefault());
  cart.addEventListener('drop', event => {
    event.preventDefault();
    addItem();
  });

  const filter = document.getElementById('filter');
  filter.addEventListener('input', () => {
    const needle = filter.value.trim().toLowerCase();

    for (const product of document.querySelectorAll('shop-product')) {
      product.hidden = needle.length > 0
        && !product.querySelector('.name').textContent.toLowerCase().includes(needle);
    }
  });

  const provenance = document.querySelector('[data-testid="provenance-list"]');
  for (let entry = 1; entry <= 40; entry += 1) {
    const row = document.createElement('li');
    row.textContent = `Batch ${entry}: forged, inspected and sealed at Rustholm.`;
    provenance.append(row);
  }
})();
