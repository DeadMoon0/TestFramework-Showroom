// The storefront's entire behaviour: pressing "Add <product> to cart" counts the item. Enough for a
// browser test to have something real to wait for, and small enough to read in one breath.
(() => {
  let items = 0;
  const summary = document.querySelector('[data-testid="cart-summary"]');

  for (const button of document.querySelectorAll('shop-product button')) {
    button.addEventListener('click', () => {
      items += 1;
      summary.textContent = items === 1 ? '1 item in cart' : `${items} items in cart`;
    });
  }

  document.querySelector('section[aria-label="Cart"] button').addEventListener('click', () => {
    summary.textContent = 'Order placed';
  });
})();
