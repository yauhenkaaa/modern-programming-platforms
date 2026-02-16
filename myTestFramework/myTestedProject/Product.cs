namespace myTestedProject
{
    // Class representing a product in an e-commerce application. It contains
    // properties for the product's ID, name, description, category, and price.
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public decimal Price { get; set; }

        public Product(int id, string name, string description, string category, decimal price)
        {
            Id = id;
            Name = name;
            Description = description;
            Category = category;
            Price = price;
        }
    }
}
