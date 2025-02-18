/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
*/

using System;
using ProtoBuf;
using System.IO;
using System.Linq;
using ProtoBuf.Meta;
using Newtonsoft.Json;
using Python.Runtime;
using NUnit.Framework;
using QuantConnect.Data;
using QuantConnect.Data.Custom;
using QuantConnect.DataSource;

namespace QuantConnect.DataLibrary.Tests
{
    [TestFixture]
    public class NasdaqDataLinkTests
    {
        [Test]
        public void JsonRoundTrip()
        {
            var expected = CreateNewInstance();
            var type = expected.GetType();
            var serialized = JsonConvert.SerializeObject(expected);
            var result = JsonConvert.DeserializeObject(serialized, type);

            AssertAreEqual(expected, result);
        }

        [Test]
        public void ProtobufRoundTrip()
        {
            var expected = CreateNewInstance();
            var type = expected.GetType();

            RuntimeTypeModel.Default[typeof(BaseData)].AddSubType(2000, type);

            using (var stream = new MemoryStream())
            {
                Serializer.Serialize(stream, expected);

                stream.Position = 0;

                var result = Serializer.Deserialize(type, stream);

                AssertAreEqual(expected, result, filterByCustomAttributes: true);
            }
        }

        [Test]
        public void Clone()
        {
            var expected = CreateNewInstance();
            var result = expected.Clone();

            AssertAreEqual(expected, result);
        }

        [Test]
        public void QuandlIdentical()
        {
            var new_ = CreateNewInstance();
            var old_ = CreateQuandlInstance();

            AssertAreEqual(new_, old_);
        }

        [Test]
        public void ValueColumn()
        {
            const int expected = 999;

            var newInstance = new IndexNasdaqDataLink();

            var symbol = Symbol.Create("UMICH/SOC1", 0, "empty");
            var config = new SubscriptionDataConfig(typeof(IndexNasdaqDataLink), symbol,
                Resolution.Daily, TimeZones.Utc, TimeZones.Utc, true, true, false, true);

            newInstance.Reader(config, "date,open,high,low,close,transactions,index", DateTime.UtcNow, false);
            var data = newInstance.Reader(config, $"2021-12-02,100,101,100,101,1000,{expected}", DateTime.UtcNow, false);

            Assert.AreEqual(expected, data.Value);
        }

        [Test]
        public void PythonValueColumn()
        {
            var expected = 999;
            dynamic instance;
            using (Py.GIL())
            {
                PyObject test = PythonEngine.ModuleFromString("testModule",
                    @"
from AlgorithmImports import *

class Test(NasdaqDataLink):
    def __init__(self):
        self.ValueColumnName = 'adj. close'").GetAttr("Test");
                instance = test.CreateType().GetBaseDataInstance();
            }

            var symbol = Symbol.Create("UMICH/SOC1", 0, "empty");
            var config = new SubscriptionDataConfig(typeof(NasdaqDataLink), symbol,
                Resolution.Daily, TimeZones.Utc, TimeZones.Utc, true, true, false, true);

            instance.Reader(config, "date,open,high,low,close,transactions,adj. close", DateTime.UtcNow, false);
            var data = instance.Reader(config, $"2021-12-02,100,101,100,101,1000,{expected}", DateTime.UtcNow, false);

            Assert.AreEqual(expected, data.Value);
        }
		
		[Test]
        public void TwoPythonValueColumn()
        {
            var ibmExpected = 999;
            var spyExpected = 111;

            dynamic ibmInstance;
            dynamic spyInstance;

            using (Py.GIL())
            {
                PyObject module = PythonEngine.ModuleFromString("testModule",
                    @"
from AlgorithmImports import *

class CustomIBM(NasdaqDataLink):
    def __init__(self):
        self.ValueColumnName = 'adj. close'

class CustomSPY(NasdaqDataLink):
    def __init__(self):
        self.ValueColumnName = 'adj. volume'");
                ibmInstance = module.GetAttr("CustomIBM").CreateType().GetBaseDataInstance();
                spyInstance = module.GetAttr("CustomSPY").CreateType().GetBaseDataInstance();
            }

            var ibm = Symbol.Create("IBM", 0, "empty");
            var spy = Symbol.Create("SPY", 0, "empty");
            var ibmConfig = new SubscriptionDataConfig(typeof(NasdaqDataLink), ibm,
                Resolution.Daily, TimeZones.Utc, TimeZones.Utc, true, true, false, true);
            var spyConfig = new SubscriptionDataConfig(typeof(NasdaqDataLink), spy,
                Resolution.Daily, TimeZones.Utc, TimeZones.Utc, true, true, false, true);

            ibmInstance.Reader(ibmConfig, "date,open,high,low,close,transactions,adj. close, adj. volume", DateTime.UtcNow, false);
            spyInstance.Reader(spyConfig, "date,open,high,low,close,transactions,adj. close, adj. volume", DateTime.UtcNow, false);
            var ibmData = ibmInstance.Reader(ibmConfig, $"2021-12-02,100,101,100,101,1000,{ibmExpected}, {spyExpected}", DateTime.UtcNow, false);
            var spyData = spyInstance.Reader(spyConfig, $"2021-12-02,100,101,100,101,1000,{ibmExpected}, {spyExpected}", DateTime.UtcNow, false);

            Assert.AreEqual(ibmExpected, ibmData.Value);
            Assert.AreEqual(spyExpected, spyData.Value);
        }

        private void AssertAreEqual(object expected, object result, bool filterByCustomAttributes = false)
        {
            foreach (var propertyInfo in expected.GetType().GetProperties())
            {
                // we skip Symbol which isn't protobuffed
                if (filterByCustomAttributes && propertyInfo.CustomAttributes.Count() != 0)
                {
                    Assert.AreEqual(propertyInfo.GetValue(expected), propertyInfo.GetValue(result));
                }
            }
            foreach (var fieldInfo in expected.GetType().GetFields())
            {
                Assert.AreEqual(fieldInfo.GetValue(expected), fieldInfo.GetValue(result));
            }
        }

        private BaseData CreateNewInstance()
        {
            return new NasdaqDataLink
            {
                Symbol = Symbol.Create("UMICH/SOC1", 0, "empty"),
                Time = new DateTime(2021, 9, 30),
                DataType = MarketDataType.Base,
                Value = 72.8m
            };
        }

        private BaseData CreateQuandlInstance()
        {
            return new Quandl
            {
                Symbol = Symbol.Create("UMICH/SOC1", 0, "empty"),
                Time = new DateTime(2021, 9, 30),
                DataType = MarketDataType.Base,
                Value = 72.8m
            };
        }

        public class IndexNasdaqDataLink : NasdaqDataLink
        {
            public IndexNasdaqDataLink() : base("index")
            {
            }
        }
    }
}
