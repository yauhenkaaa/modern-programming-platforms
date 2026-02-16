namespace myTestedProject
{
    public class InventoryService
    {
        /// <summary>
        /// Shared mutable state used to verify that the same instance is injected across test classes.
        /// </summary>
        public int SharedState { get; set; }

        /// <summary>
        /// This method simulates checking the stock availability of a product by its ID. 
        /// It is an asynchronous method that returns a Task<bool>.
        /// </summary>
        /// <param name="productId"></param>
        /// <returns></returns>
        public async Task<bool> CheckStockAsync(int productId)
        {
            await Task.Delay(500);

            if (productId <= 0 || productId == 999)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// This method simulates processing an order for a product by its ID. 
        /// It is an asynchronous method that returns a Task.
        /// </summary>
        /// <param name="productId"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task ProcessOrderAsync(int productId)
        {
            await Task.Delay(200);
            if (productId == 0)
            {
                throw new InvalidOperationException("Invalid product ID");
            }
        }
    }
}
