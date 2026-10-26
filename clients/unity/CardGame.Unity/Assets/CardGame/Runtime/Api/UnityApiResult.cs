using System.Collections.Generic;
using Game.UnityClient.Contracts;

namespace CardGame.Unity.Client.Api
{
  public sealed class UnityApiResult<T>
  {
    public bool Succeeded { get; private set; }
    public T Value { get; private set; }
    public UnityError Error { get; private set; }
    public int HttpStatus { get; private set; }
    public string RawBody { get; private set; }

    public bool HasValue => Succeeded && EqualityComparer<T>.Default.Equals(Value, default) == false;

    private UnityApiResult(bool succeeded, T value, UnityError error, int httpStatus, string rawBody)
    {
      Succeeded = succeeded;
      Value = value;
      Error = error;
      HttpStatus = httpStatus;
      RawBody = rawBody;
    }

    public static UnityApiResult<T> Success(T value, int httpStatus, string rawBody) =>
      new(true, value, new UnityError(), httpStatus, rawBody);

    public static UnityApiResult<T> Failure(UnityError error, int httpStatus, string rawBody) =>
      new(false, default, error, httpStatus, rawBody);
  }
}
