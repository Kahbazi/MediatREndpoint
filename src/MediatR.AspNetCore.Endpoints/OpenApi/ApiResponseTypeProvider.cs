using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace MediatR.AspNetCore.Endpoints.OpenApi
{
    internal class ApiResponseTypeProvider
    {
        public ICollection<ApiResponseType> GetApiResponseTypes(ControllerActionDescriptor action, MediatorEndpoint mediatorEndpoint)
        {
            var declaredReturnType = mediatorEndpoint.ResponseType;

            var runtimeReturnType = GetRuntimeReturnType(declaredReturnType);

            var responseMetadataAttributes = GetResponseMetadataAttributes(action);
            if (!HasSignificantMetadataProvider(responseMetadataAttributes) &&
                action.Properties.TryGetValue(typeof(ApiConventionResult), out var result))
            {
                // Action does not have any conventions. Use conventions on it if present.
                var apiConventionResult = (ApiConventionResult)result;
                responseMetadataAttributes.AddRange(apiConventionResult.ResponseMetadataProviders);
            }

            var defaultErrorType = typeof(void);
            if (action.Properties.TryGetValue(typeof(ProducesErrorResponseTypeAttribute), out result))
            {
                defaultErrorType = ((ProducesErrorResponseTypeAttribute)result).Type;
            }

            var apiResponseTypes = GetApiResponseTypes(responseMetadataAttributes, runtimeReturnType, defaultErrorType);
            return apiResponseTypes;
        }

        private static List<IApiResponseMetadataProvider> GetResponseMetadataAttributes(ControllerActionDescriptor action)
        {
            if (action.FilterDescriptors == null)
            {
                return new List<IApiResponseMetadataProvider>();
            }

            // This technique for enumerating filters will intentionally ignore any filter that is an IFilterFactory
            // while searching for a filter that implements IApiResponseMetadataProvider.
            //
            // The workaround for that is to implement the metadata interface on the IFilterFactory.
            return action.FilterDescriptors
                .Select(fd => fd.Filter)
                .OfType<IApiResponseMetadataProvider>()
                .ToList();
        }

        private ICollection<ApiResponseType> GetApiResponseTypes(
           IReadOnlyList<IApiResponseMetadataProvider> responseMetadataAttributes,
           Type type,
           Type defaultErrorType)
        {
            var results = new Dictionary<int, ApiResponseType>();

            // Get the content type that the action explicitly set to support.
            // Walk through all 'filter' attributes in order, and allow each one to see or override
            // the results of the previous ones. This is similar to the execution path for content-negotiation.
            var contentTypes = new MediaTypeCollection();
            if (responseMetadataAttributes != null)
            {
                foreach (var metadataAttribute in responseMetadataAttributes)
                {
                    metadataAttribute.SetContentTypes(contentTypes);

                    var statusCode = metadataAttribute.StatusCode;

                    var apiResponseType = new ApiResponseType
                    {
                        Type = metadataAttribute.Type,
                        StatusCode = statusCode,
                        IsDefaultResponse = metadataAttribute is IApiDefaultResponseMetadataProvider,
                    };

                    if (apiResponseType.Type == typeof(void))
                    {
                        if (type != null && (statusCode == StatusCodes.Status200OK || statusCode == StatusCodes.Status201Created))
                        {
                            // ProducesResponseTypeAttribute's constructor defaults to setting "Type" to void when no value is specified.
                            // In this event, use the action's return type for 200 or 201 status codes. This lets you decorate an action with a
                            // [ProducesResponseType(201)] instead of [ProducesResponseType(201, typeof(Person)] when typeof(Person) can be inferred
                            // from the return type.
                            apiResponseType.Type = type;
                        }
                        else if (IsClientError(statusCode) || apiResponseType.IsDefaultResponse)
                        {
                            // Use the default error type for "default" responses or 4xx client errors if no response type is specified.
                            apiResponseType.Type = defaultErrorType;
                        }
                    }

                    if (apiResponseType.Type != null)
                    {
                        results[apiResponseType.StatusCode] = apiResponseType;
                    }
                }
            }

            // Set the default status only when no status has already been set explicitly
            if (results.Count == 0 && type != null)
            {
                results[StatusCodes.Status200OK] = new ApiResponseType
                {
                    StatusCode = StatusCodes.Status200OK,
                    Type = type,
                };
            }

            if (contentTypes.Count == 0)
            {
                // None of the IApiResponseMetadataProvider specified a content type. This is common for actions that
                // specify one or more ProducesResponseType but no ProducesAttribute. In this case, formatters will participate in conneg
                // and respond to the incoming request.
                // Querying IApiResponseTypeMetadataProvider.GetSupportedContentTypes with "null" should retrieve all supported
                // content types that each formatter may respond in.
                contentTypes.Add((string)null);
            }

            var responseTypes = results.Values;
            CalculateResponseFormats(responseTypes, contentTypes);
            return responseTypes;
        }

        private void CalculateResponseFormats(ICollection<ApiResponseType> responseTypes, MediaTypeCollection declaredContentTypes)
        {
            foreach (var apiResponse in responseTypes)
            {
                var responseType = apiResponse.Type;
                if (responseType == null || responseType == typeof(void))
                {
                    continue;
                }

                //apiResponse.ModelMetadata = _modelMetadataProvider.GetMetadataForType(responseType);

                foreach (var contentType in declaredContentTypes)
                {
                    var isSupportedContentType = false;

                    apiResponse.ApiResponseFormats.Add(new ApiResponseFormat
                    {

                        MediaType = "application/json",
                    });

                    if (!isSupportedContentType && contentType != null)
                    {
                        // No output formatter was found that supports this content type. Add the user specified content type as-is to the result.
                        apiResponse.ApiResponseFormats.Add(new ApiResponseFormat
                        {
                            MediaType = contentType,
                        });
                    }
                }
            }
        }

        private Type GetRuntimeReturnType(Type declaredReturnType)
        {
            // If we get here, then a filter didn't give us an answer, so we need to figure out if we
            // want to use the declared return type.
            //
            // We've already excluded Task, void, and IActionResult at this point.
            //
            // If the action might return any object, then assume we don't know anything about it.
            if (declaredReturnType == typeof(object))
            {
                return null;
            }

            return declaredReturnType;
        }

        private static bool IsClientError(int statusCode)
        {
            return statusCode >= 400 && statusCode < 500;
        }

        private static bool HasSignificantMetadataProvider(IReadOnlyList<IApiResponseMetadataProvider> providers)
        {
            for (var i = 0; i < providers.Count; i++)
            {
                var provider = providers[i];

                if (provider is ProducesAttribute producesAttribute && producesAttribute.Type is null)
                {
                    // ProducesAttribute that does not specify type is considered not significant.
                    continue;
                }

                // Any other IApiResponseMetadataProvider is considered significant
                return true;
            }

            return false;
        }
    }
}
