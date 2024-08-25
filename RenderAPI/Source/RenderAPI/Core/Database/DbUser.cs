namespace RenderAPI.Core.Database
{
    public class DbUser
    {
        public UInt16 UserId { get; set; }
        public String FirstName { get; set; }
        public String LastName { get; set; }
        public String Email { get; set; }
        public Byte SubscriptionId { get; set; }
        public Boolean IsActive { get; set; }
        public String Token { get; set; }

        public DbUser(UInt16 userId, String firstName, String lastName, String email, Byte subscriptionId, Boolean isActive, String token)
        {
            UserId = userId;
            FirstName = firstName;
            LastName = lastName;
            Email = email;
            SubscriptionId = subscriptionId;
            IsActive = isActive;
            Token = token;
        }
    }
}
