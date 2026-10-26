namespace BeanShare.Application.Common;

/// <summary>Builds a failed <see cref="Result"/> or <see cref="Result{T}"/> for a pipeline's response type.</summary>
public static class ResultFactory
{
    public static TResponse Failure<TResponse>(Error error) => Failure<TResponse>(new[] { error });

    public static TResponse Failure<TResponse>(IEnumerable<Error> errors)
    {
        var responseType = typeof(TResponse);

        if (responseType == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(errors);
        }

        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var resultType = responseType.GetGenericArguments()[0];
            var failureMethod = typeof(Result<>).MakeGenericType(resultType)
                .GetMethod("Failure", new[] { typeof(IEnumerable<Error>) });

            if (failureMethod != null)
            {
                var result = failureMethod.Invoke(null, new object[] { errors });
                return (TResponse)result!;
            }
        }

        throw new InvalidOperationException($"Cannot create failure result for type {responseType}");
    }

    public static bool CanCreate<TResponse>()
    {
        var responseType = typeof(TResponse);
        return responseType == typeof(Result)
            || (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>));
    }
}
