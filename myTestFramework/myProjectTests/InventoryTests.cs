using myTestedProject;
using myTestingLibrary;
using myTestingLibrary.Attributes;

namespace myProjectTests
{
    [TestClass]
    public class InventoryTests
    {
        [SharedContext]
        private InventoryService _inventory;

        /// <summary>
        /// Sets shared state so that OrderTests.TestSharedContextReadFromOtherClass 
        /// can verify the same instance (this class runs before OrderTests).
        /// </summary>
        [TestMethod]
        public void TestSharedContextWriteVisibleInOtherClass()
        {
            Thread.Sleep(1000);
            _inventory.SharedState = 200;
            Assertion.AreEqual(200, _inventory.SharedState);
        }

        // Очень строгий таймаут (1 мс), а внутри — искусственная задержка,
        // чтобы гарантированно показать срабатывание механизма таймаута.
        [TestMethod(timeout: 10000)]
        public async Task TestCheckStockAsync()
        {
            await Task.Delay(2000); // имитация долгой операции
            bool inStock = await _inventory.CheckStockAsync(1);
            Assertion.IsTrue(inStock);
        }
    }
}
