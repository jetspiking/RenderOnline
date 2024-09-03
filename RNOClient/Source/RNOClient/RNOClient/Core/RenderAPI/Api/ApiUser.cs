using System;

namespace RNOClient.Core.RenderAPI.Api
{
    public class ApiUser
    {
        public UInt16 UserId { get; set; }
        public String FirstName { get; set; }
        public String LastName { get; set; }
        public String Email { get; set; }
        public Byte SubscriptionId { get; set; }
        public Boolean IsActive { get; set; }

        public ApiUser(UInt16 userId, String firstName, String lastName, String email, Byte subscriptionId, Boolean isActive)
        {
            UserId = userId;
            FirstName = firstName;
            LastName = lastName;
            Email = email;
            SubscriptionId = subscriptionId;
            IsActive = isActive;
        }
    }
}
