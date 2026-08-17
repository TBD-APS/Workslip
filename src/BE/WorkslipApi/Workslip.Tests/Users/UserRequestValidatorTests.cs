using Workslip.Application.Users;
using Workslip.Application.Users.Validators;
using Workslip.Domain;
using Xunit;

namespace Workslip.Tests.Users;

// WOR-412: DisplayName is a 200-char column (SqlDbContext HasMaxLength(200)), so the
// request validators must reject > 200 rather than letting a 201–256 char name reach EF
// and fail with a truncation 500. Phone must be numeric, not free text.
public sealed class UserRequestValidatorTests
{
    private static CreateUserRequest CreateRequest(
        string displayName = "Niels Petersen",
        string phone = "+45 12 34 56 78") =>
        new("niels@example.dk", displayName, phone, Roles.User);

    [Fact]
    public void CreateUser_DisplayNameOver200_IsRejected()
    {
        var result = new CreateUserRequestValidator()
            .Validate(CreateRequest(displayName: new string('a', 201)));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateUserRequest.DisplayName));
    }

    [Fact]
    public void CreateUser_DisplayNameExactly200_IsAccepted()
    {
        var result = new CreateUserRequestValidator()
            .Validate(CreateRequest(displayName: new string('a', 200)));

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreateUserRequest.DisplayName));
    }

    [Theory]
    [InlineData("12ab34")]
    [InlineData("ring til mig")]
    public void CreateUser_NonNumericPhone_IsRejected(string phone)
    {
        var result = new CreateUserRequestValidator().Validate(CreateRequest(phone: phone));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateUserRequest.Phone));
    }

    [Theory]
    [InlineData("+45 12 34 56 78")]
    [InlineData("12345678")]
    [InlineData("(045) 12-34-56")]
    public void CreateUser_NumericPhone_IsAccepted(string phone)
    {
        var result = new CreateUserRequestValidator().Validate(CreateRequest(phone: phone));

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(CreateUserRequest.Phone));
    }

    [Fact]
    public void UpdateUser_DisplayNameOver200_IsRejected()
    {
        var result = new UpdateUserRequestValidator()
            .Validate(new UpdateUserRequest(new string('a', 201), null, null));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserRequest.DisplayName));
    }

    [Fact]
    public void UpdateUser_NullDisplayName_SkipsLengthRule()
    {
        var result = new UpdateUserRequestValidator()
            .Validate(new UpdateUserRequest(null, null, null));

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(UpdateUserRequest.DisplayName));
    }
}
