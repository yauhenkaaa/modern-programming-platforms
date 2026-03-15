using myTestedProject;
using myTestingLibrary;
using myTestingLibrary.Attributes;

namespace myProjectTests
{
    [TestClass]
    public class OrderTests
    {
        [SharedContext]
        private InventoryService _inventory;

        /// <summary>
        /// [BeforeEach] uses the injected [SharedContext] _inventory to verify it is available.
        /// </summary>
        [BeforeEach]
        public void VerifySharedContextInjected()
        {
            Assertion.IsNotNull(_inventory);
        }

        /// <summary>
        /// This method tests the CheckStockAsync method of the InventoryService class using the shared context instance.
        /// </summary>
        [TestClass(timeout: 1)]
        [TestMethod]
        public async Task TestStockCheckAsync()
        {
            await Task.Delay(200000);
            bool result = await _inventory.CheckStockAsync(1);
            Assertion.IsTrue(result);
        }

        /// <summary>
        /// This method tests the CreateOrderAsync method of the OrderManager class using the shared _inventory.
        /// </summary>
        [TestClass(timeout: 2000)]
        [TestMethod]
        public async Task TestOrderResultType()
        {
            var manager = new OrderManager(_inventory);

            var user = new UserAccount();
            user.SetEmail("test@example.com");

            var cart = new Cart();
            cart.AddProduct(new Product(0, "Banana", "Tasty bananas right from Ecuador", "Fruits", 3.49m));

            var result = await manager.CreateOrderAsync(user, cart);
            Assertion.IsInstanceOfType(typeof(string), result);
        }

        /// <summary>
        /// Verifies that state set in InventoryTests is visible here (same shared instance; InventoryTests runs first).
        /// </summary>
        [TestMethod]
        public void TestSharedContextReadFromOtherClass()
        {
            Assertion.AreEqual(200, _inventory.SharedState);
        }
    }
}
