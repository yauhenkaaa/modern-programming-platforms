namespace myTestedProject
{
    public class DiscountService
    {
        /// <summary>
        /// This method applies a discount to the current total based on the provided coupon code.
        /// </summary>
        /// <param name="code"></param>
        /// <param name="currentTotal"></param>
        /// <returns></returns>
        public decimal ApplyCoupon(string code, decimal currentTotal)
        {
            if (string.IsNullOrWhiteSpace(code)) return currentTotal;

            // Тест: Assert.AreEqual(90, service.ApplyCoupon("SAVE10", 100))
            if (code == "SAVE10") return currentTotal * 0.9m;
            if (code == "SAVE50") return currentTotal * 0.5m;

            return currentTotal;
        }

        /// <summary>
        /// This method checks if a given coupon code is expired. For the sake of this example, 
        /// we will consider any coupon code that contains "2023" as expired, while others are valid.
        /// </summary>
        /// <param name="code"></param>
        /// <returns></returns>
        public bool IsCouponExpired(string code)
        {
            // Тест: Assert.IsFalse(service.IsCouponExpired("NEW_YEAR_2025"))
            return code.Contains("2023");
        }
    }
}
