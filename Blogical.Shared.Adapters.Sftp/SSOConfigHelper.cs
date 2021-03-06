﻿//---------------------------------------------------------------------
// File: SSOConfigHelper.cs
// 
// Summary: SSOConfigHelper class for reading/writing cofiguration values to/from SSO
//
// Sample: SSO as Configuration Store (BizTalk Server Sample)   
//
//---------------------------------------------------------------------
// This file is part of the Microsoft BizTalk Server 2006 SDK
//
// Copyright (c) Microsoft Corporation. All rights reserved.
//
// This source code is intended only as a supplement to Microsoft BizTalk
// Server 2006 release and/or on-line documentation. See these other
// materials for detailed information regarding Microsoft code samples.
//
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY
// KIND, WHETHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR
// PURPOSE.
//---------------------------------------------------------------------


using System;
using System.Collections.Specialized;
using Microsoft.BizTalk.SSOClient.Interop;

namespace Blogical.Shared.Adapters.Sftp
{
    public class ConfigurationPropertyBag : IPropertyBag
    {
        private readonly HybridDictionary _properties;
        internal ConfigurationPropertyBag()
        {
            _properties = new HybridDictionary();
        }
        public void Read(string propName, out object ptrVar, int errLog)
        {
            ptrVar = _properties[propName];
        }
        public void Write(string propName, ref object ptrVar)
        {
            _properties.Add(propName, ptrVar);
        }
        public bool Contains(string key)
        {
            return _properties.Contains(key);
        }
        public void Remove(string key)
        {
            _properties.Remove(key);
        }
    }


    public static class SSOConfigHelper
    {
        /// <summary>
        /// Struct to hold username+password credentials.
        /// </summary>
        public struct Credentials
        {
            public string Username;
            public string Password;
        }

        /// <summary>
        /// Retrieves the credentials to use.
        /// </summary>
        /// <param name="appName">The name of the affiliate application to represent the configuration container to access</param>
        /// <returns>Credentials to use.</returns>
        public static Credentials GetCredentials(string appName)
        {
            Credentials credentials;
            try
            {
                ISSOLookup1 ssoLookup = (ISSOLookup1)new SSOLookup();
                var passwords = ssoLookup.GetCredentials(appName, 0, out credentials.Username);
                credentials.Password = passwords[0];
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.WriteLine(e.Message);
                throw;
            }
            return credentials;
        }
    }
}
