using myTestedProject;
using myTestingLibrary;
using myTestingLibrary.Attributes;
using System.Threading;

namespace myProjectTests
{
    [TestClass]
    public class UserTests
    {
        /// <summary>
        /// This method tests the creation of a UserAccount instance. It creates a new UserAccount object,
        /// sets its email property to "babayka123@gmail.com" and asserts that the user object is not null 
        /// using the IsNotNull method of the Assertion class.
        /// </summary>
        [TestMethod]
        public void TestUserCreation()
        {
            UserAccount user = new UserAccount();
            user.SetEmail("babayka123@gmail.com");
            Thread.Sleep(2000);
            Assertion.IsNotNull(user);
        }
        /// <summary>
        /// This method tests the GetWelcomeMessage method of the UserAccount class. It creates a new UserAccount instance,
        /// and sets the IsPremium property to true. It then calls the GetWelcomeMessage method and asserts that the returned 
        /// message contains the word "Premium" using the Contains method of the Assertion class.
        /// </summary>
        [TestMethod]
        public void TestWelcomeMessage()
        {
            UserAccount user = new UserAccount();
            user.IsPremium = true;
            Thread.Sleep(2000);
            string message = user.GetWelcomeMessage();
            Assertion.Contains("Premium", message);
        }

        /// <summary>
        /// This method tests the SetEmail method of the UserAccount class. It creates a new UserAccount instance 
        /// and attempts to set an invalid email address ("invalidemail") using the SetEmail method. 
        /// The test asserts that an ArgumentException is thrown when trying to set the invalid email,
        /// indicating that the email format is not valid.
        /// </summary>
        [TestMethod]
        public void TestInvalidEmail()
        {
            UserAccount user = new UserAccount();
            Thread.Sleep(2000);
            Assertion.Throws<ArgumentException>(() => user.SetEmail("invalidemail"));
        }

        /// <summary>
        /// This method tests the GetWelcomeMessage method of the UserAccount class for a guest user. 
        /// It creates a new UserAccount instance, and sets the IsPremium property to false. It then calls 
        /// the GetWelcomeMessage method and asserts that the returned message contains the word "Guest" using the 
        /// Contains method of the Assertion class.
        /// </summary>
        [TestMethod]
        public void TestWelcomeMessageForGuest()
        {
            UserAccount user = new UserAccount();
            user.IsPremium = false;
            Thread.Sleep(2000);
            string message = user.GetWelcomeMessage();
            Assertion.Contains("Guest", message);
        }

        /// <summary>
        /// This method tests the IsCouponExpired method of the DiscountService class. It creates
        /// the DiscountService instance and calls the IsCouponExpired method with the coupon code "NEW_YEAR_2025".
        /// </summary>
        [TestMethod]
        public void TestCouponExpiry()
        {   
            DiscountService service = new DiscountService();
            Thread.Sleep(2000);
            Assertion.IsFalse(service.IsCouponExpired("NEW_YEAR_2025"));
        }

        public static IEnumerable<object[]> GetDiscountData()
        {
            yield return new object[] { "SAVE10", 100m, 90m };
            yield return new object[] { "SAVE50", 100m, 50m };
            yield return new object[] { "INVALID", 100m, 100m };
        }

        // Добавьте сам параметризованный тест
        [TestMethod]
        [DynamicData(nameof(GetDiscountData))]
        public void TestApplyCouponParameterized(string code, decimal total, decimal expected)
        {
            DiscountService service = new DiscountService();
            Assertion.AreEqual(expected, service.ApplyCoupon(code, total));
        }
    }
}
