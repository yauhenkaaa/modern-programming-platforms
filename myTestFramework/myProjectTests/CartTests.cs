using myTestedProject;
using myTestingLibrary;
using myTestingLibrary.Attributes;
using System.Collections.ObjectModel;

namespace myProjectTests
{
    [TestClass]
    public class CartTests
    {
        private Cart _cart;

        [BeforeEach]
        public void Setup()
        {
            _cart = new Cart();
        }

        [AfterEach]
        public void TearDown()
        {
            _cart = null;
        }

        /// <summary>
        /// This method tests the GetTotal method of the Cart class. It creates
        /// a new Cart instance and adds two products to it: a banana priced at 
        /// $3.49 and an apple priced at $7.49.
        /// </summary>
        [TestMethod]
        public void TestTotalSum()
        {
            Product product = new Product(0, "Banana", "Tasty bananas right from Ecuador", "Fruits", 3.49m);
            _cart.AddProduct(product);

            product = new Product(1, "Apple", "Juicy Turkish apples", "Fruits", (decimal)7.49);
            _cart.AddProduct(product);

            Assertion.AreEqual((decimal)10.98, _cart.GetTotal());
        }

        /// <summary>
        /// This method tests the equality of two Product instances. It creates two products with different 
        /// properties and asserts that they are not equal using the AreNotEqual method of the Assertion class.
        /// </summary>
        [TestMethod]
        public void TestProductInequality()
        {
            Product product1 = new Product(0, "Banana", "Tasty bananas right from Ecuador", "Fruits", 3.49m);
            Product product2 = new Product(1, "Apple", "Juicy Turkish apples", "Fruits", 7.49m);

            Assertion.AreNotEqual(product1, product2);
        }

        /// <summary>
        /// This method tests the RemoveProduct method of the Cart class. 
        /// It creates a new Cart instance, adds a product to it, and then attempts to remove the product using its ID. 
        /// The test asserts that the RemoveProduct method returns true, indicating that the product was successfully 
        /// removed from the cart.
        /// </summary>
        [TestMethod]
        public void TestRemoveSuccess()
        {
            Product product = new Product(0, "Banana", "Tasty bananas right from Ecuador", "Fruits", 3.49m);
            _cart.AddProduct(product);
            bool result = _cart.RemoveProduct(0);
            Assertion.IsTrue(result);
        }

        /// <summary>
        /// This method tests the RemoveProduct method of the Cart class when attempting to remove a product 
        /// that does not exist in the cart.
        /// </summary>
        [TestMethod]
        public void TestRemoveFailure()
        {
            Product product = new Product(0, "Banana", "Tasty bananas right from Ecuador", "Fruits", 3.49m);
            _cart.AddProduct(product);
            bool result = _cart.RemoveProduct(1);
            Assertion.IsFalse(result);
        }

        /// <summary>
        /// This method tests that the cart is not empty after adding a product. 
        /// It creates a new Cart instance, adds a product to it,
        /// and then retrieves the items in the cart as a collection. The test asserts
        /// that the collection of items is not empty using the IsNotEmpty method of the Assertion class.
        /// </summary>
        [TestMethod]
        public void TestCartIsNotEmpty()
        {
            Product product = new Product(0, "Banana", "Tasty bananas right from Ecuador", "Fruits", 3.49m);
            _cart.AddProduct(product);
            Collection<Product> items = new Collection<Product>(_cart.Items.ToList());
            Assertion.IsNotEmpty(items);
        }

        /// <summary>
        /// This method tests that adding a product with a negative price to the cart throws an ArgumentException.
        /// </summary>
        [TestMethod]
        public void TestNegativePriceException()
        {
            var badProduct = new Product(0, "Banana", "Tasty bananas right from Ecuador", "Fruits", -3.49m);

            Assertion.Throws<ArgumentException>(() =>
            {
                _cart.AddProduct(badProduct);
            });
        }

        /// <summary>
        /// This method tests that searching for a product 
        /// that does not exist in the cart returns null.
        /// </summary>
        [TestMethod]
        public void TestFindMissingProduct()
        {
            Product product = new Product(0, "Banana", "Tasty bananas right from Ecuador", "Fruits", 3.49m);
            _cart.AddProduct(product);
            var foundProduct = _cart.Items.FirstOrDefault(p => p.Id == 1);
            Assertion.IsNull(foundProduct);
        }
    }
}

