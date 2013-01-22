﻿namespace NServiceBus.IntegrationTests.Support
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    [Serializable]
    public class RunDescriptor : MarshalByRefObject
    {
        public RunDescriptor()
        {
            Settings = new Dictionary<string, string>();
        }

        public RunDescriptor(RunDescriptor template)
        {
            Settings = template.Settings.ToDictionary(entry => entry.Key,
                                                      entry => entry.Value);
            Key = template.Key;
        }

        public string Key { get; set; }

        public IDictionary<string, string> Settings { get; set; }

        public string Permutation
        {
            get
            {
                if (permutation == "0")
                    return "";

                return permutation;
            }
            set { permutation = value; }
        }

        string permutation;

        public void Merge(RunDescriptor descriptorToAdd)
        {
            Key += "." + descriptorToAdd.Key;

            foreach (var setting in descriptorToAdd.Settings)
            {
                Settings[setting.Key] = setting.Value;
            }
        }
    }
}