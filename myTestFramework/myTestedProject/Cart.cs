namespace myTestedProject
{
    // This class represents a shopping cart that can hold multiple products.
    // It provides methods to add, remove, and find products in the cart,
    // as well as calculate the total price of the items in the cart.
    public class Cart
    {
        private readonly List<Product> _items = new List<Product>();

        // Readonly property to access the items in the cart
        public IEnumerable<Product> Items => _items;

        /// <summary>
        /// This method adds a product to the cart. It takes a Product object as a parameter and adds it to the _items list.
        /// </summary>
        /// <param name="product"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public void AddProduct(Product product)
        {
            if (product == null)
                throw new ArgumentNullException(nameof(product));

            if (product.Price < 0)
                throw new ArgumentException("Product price can't be negative.");

            _items.Add(product);
        }

        /// <summary>
        /// This method removes a product from the cart based on its ID. It takes an integer productId as a parameter,
        /// and searches for a product with that ID in the _items list. If a product with the specified ID is found, 
        /// it is removed from the list and the method returns true.
        /// </summary>
        /// <param name="productId"></param>
        /// <returns></returns>
        public bool RemoveProduct(int productId)
        {
            var product = _items.FirstOrDefault(p => p.Id == productId);
            if (product == null)
            {
                return false;
            }

            _items.Remove(product);
            return true;
        }

        /// <summary>
        /// This method calculates the total price of all products in the cart. 
        /// It uses LINQ to sum the Price property of each product in the 
        /// _items list and returns the total as a decimal value.
        /// </summary>
        /// <returns></returns>
        public decimal GetTotal()
        {
            return _items.Sum(p => p.Price);
        }

        /// <summary>
        /// This method finds a product in the cart by its name. It takes a string name as a 
        /// parameter and uses LINQ to search for a product in the _items list whose Name property contains 
        /// the specified name (case-insensitive). 
        /// If a matching product is found, it is returned; otherwise, the method returns null.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public Product FindByName(string name)
        {
            return _items.FirstOrDefault(p => p.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// This method clears all products from the cart by calling the Clear method on the _items list,
        /// and effectively removing all products from the cart. After calling this method, the cart will be empty.
        /// </summary>
        public void Clear()
        {
            _items.Clear();
        }
    }
}
