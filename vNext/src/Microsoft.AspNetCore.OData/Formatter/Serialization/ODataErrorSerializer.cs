﻿// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Runtime.Serialization;
using Microsoft.OData.Core;
using Microsoft.AspNetCore.OData.Common;
using Microsoft.AspNetCore.OData.Extensions;

namespace Microsoft.AspNetCore.OData.Formatter.Serialization
{
    /// <summary>
    /// Represents an <see cref="ODataSerializer"/> to serialize <see cref="ODataError"/>s=.
    /// </summary>
    public class ODataErrorSerializer : ODataSerializer
    {
        /// <summary>
        /// Initializes a new instance of the class <see cref="Microsoft.OData.Core.ODataSerializer"/>.
        /// </summary>
        public ODataErrorSerializer()
            : base(ODataPayloadKind.Error)
        {
        }

        /// <inheritdoc/>
        public override void WriteObject(object graph, Type type, ODataMessageWriter messageWriter, ODataSerializerContext writeContext)
        {
            if (graph == null)
            {
                throw Error.ArgumentNull("graph");
            }
            if (messageWriter == null)
            {
                throw Error.ArgumentNull("messageWriter");
            }

            var oDataError = graph as ODataError;
            if (oDataError == null)
            {
                var message = Error.Format(SRResources.ErrorTypeMustBeODataErrorOrHttpError, graph.GetType().FullName);
                throw new SerializationException(message);
                
            }

            var includeDebugInformation = oDataError.InnerError != null;
            messageWriter.WriteError(oDataError, includeDebugInformation);
        }
    }
}
