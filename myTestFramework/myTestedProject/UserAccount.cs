namespace myTestedProject
{
    public class UserAccount
    {
        public string Email { get; private set; }
        public bool IsPremium { get; set; }

        /// <summary>
        /// This method sets the email address for the user account. 
        /// It takes a string email as a parameter and assigns it to the Email property.
        /// </summary>
        /// <param name="email"></param>
        /// <exception cref="ArgumentException"></exception>
        public void SetEmail(string email)
        {
            if (!email.Contains("@"))
                throw new ArgumentException("Invalid email format");

            Email = email;
        }

        /// <summary>
        /// This method generates a welcome message for the user based on their account type.
        /// </summary>
        /// <returns></returns>
        public string GetWelcomeMessage()
        {
            // Тест: Assert.Contains("Premium", user.GetWelcomeMessage())
            return IsPremium ? "Welcome, Premium User!" : "Welcome, Guest!";
        }
    }
}