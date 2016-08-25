// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.OData.Builder.Conventions;
using Microsoft.OData.Core;
using Microsoft.OData.Core.UriParser;
using Microsoft.AspNetCore.OData.Common;

namespace Microsoft.AspNetCore.OData.Formatter
{
    internal class DefaultODataETagHandler : IETagHandler
    {
        /// <summary>null liternal that needs to be return in ETag value when the value is null</summary>
        private const string NullLiteralInETag = "null";

        private const char Separator = ',';

        public EntityTagHeaderValue CreateETag(IDictionary<string, object> properties)
        {
            if (properties == null)
            {
                throw Error.ArgumentNull("properties");
            }

            if (properties.Count == 0)
            {
                return null;
            }

            var builder = new StringBuilder();
            builder.Append('\"');
            var firstProperty = true;

            foreach (var propertyValue in properties.Values)
            {
                if (firstProperty)
                {
                    firstProperty = false;
                }
                else
                {
                    builder.Append(Separator);
                }

                var str = propertyValue == null
                    ? NullLiteralInETag
                    : ConventionsHelpers.GetUriRepresentationForValue(propertyValue);

                // base64 encode
                var bytes = Encoding.UTF8.GetBytes(str);
                var etagValueText = Convert.ToBase64String(bytes);
                builder.Append(etagValueText);
            }

            builder.Append('\"');
            var tag = builder.ToString();
            return new EntityTagHeaderValue(tag, isWeak: true);
        }

        public IDictionary<string, object> ParseETag(EntityTagHeaderValue etagHeaderValue)
        {
            if (etagHeaderValue == null)
            {
                throw Error.ArgumentNull("etagHeaderValue");
            }

            var tag = etagHeaderValue.Tag.Trim('\"');

            // split etag
            var rawValues = tag.Split(Separator);
            IDictionary<string, object> properties = new Dictionary<string, object>();
            for (var index = 0; index < rawValues.Length; index++)
            {
                var rawValue = rawValues[index];

                // base64 decode
                var bytes = Convert.FromBase64String(rawValue);
                var valueString = Encoding.UTF8.GetString(bytes);
                var obj = ODataUriUtils.ConvertFromUriLiteral(valueString, ODataVersion.V4);
                if (obj is ODataNullValue)
                {
                    obj = null;
                }
                properties.Add(index.ToString(CultureInfo.InvariantCulture), obj);
            }

            return properties;
        }
    }
}
