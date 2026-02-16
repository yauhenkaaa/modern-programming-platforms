namespace myTestedProject
{
    public class OrderManager
    {
        private readonly InventoryService _inventory;

        public OrderManager(InventoryService inventory)
        {
            _inventory = inventory;
        }

        /// <summary>
        /// This method creates an order for a user based on the items in their cart. 
        /// It performs several checks, such as verifying that the user has an email and that the cart is not empty. 
        /// It then simulates an asynchronous check for each item in the cart to see if it is in stock. 
        /// If any item is out of stock, it returns a failure message. 
        /// If all items are available, it generates and returns a success message with a unique order ID.
        /// </summary>
        /// <param name="user"></param>
        /// <param name="cart"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<string> CreateOrderAsync(UserAccount user, Cart cart)
        {
            if (user.Email == null) throw new InvalidOperationException("User must have email");
            if (!cart.Items.Any()) throw new InvalidOperationException("Cart is empty");

            foreach (var item in cart.Items)
            {
                bool available = await _inventory.CheckStockAsync(item.Id);
                if (!available) return "Failed: Out of stock";
            }

            return $"Order_SUCCESS_{Guid.NewGuid().ToString().Substring(0, 8)}";
        }
    }
}
