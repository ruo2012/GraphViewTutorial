﻿using System.Collections.Generic;
using System.Linq;
using GraphView;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

// //------------------------------------------------------------
// // Copyright (c) Microsoft Corporation.  All rights reserved.
// //------------------------------------------------------------
namespace GraphViewUnitTest.Gremlin.ProcessTests.Traversal.Step.Map
{
    /// <summary>
    /// Tests for the Select Step.
    /// </summary>
    [TestClass]
    public class SelectTest : AbstractGremlinTest
    {
        /// <summary>
        /// Port of the g_VX1X_asXaX_outXknowsX_asXbX_selectXa_bX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V(v1Id).as('a').out('knows').as('b').select('a','b')", "v1Id", v1Id
        /// </summary>
        /// <remarks>
        /// This fails because Select for more than 1 key is not yet implemented.
        /// WorkItem to track this: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/37285
        /// </remarks>
        [TestMethod]
        [Ignore]
        public void HasVertexIdAsAOutKnowsAsBSelectAB()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                string markoVertexId = this.ConvertToVertexId(graphCommand, "marko");
                ////string vadasVertexId = this.ConvertToVertexId(graphCommand, "vadas");
                ////string joshVertexId = this.ConvertToVertexId(graphCommand, "josh");

                graphCommand.OutputFormat = OutputFormat.GraphSON;
                var traversal = graphCommand.g().V()
                    .HasId(markoVertexId).As("a")
                    .Out("knows").As("b")
                    .Select("a", "b");

                var result = traversal.Next();
                dynamic dynamicResult = JsonConvert.DeserializeObject<dynamic>(result.FirstOrDefault());
                Assert.AreEqual(2, dynamicResult.Count);
                foreach (var res in dynamicResult)
                {
                    Assert.AreEqual(2, res.Count);
                    // Skipping this validation until we can fix the multi-key select.
                    ////Assert.AreEqual(markoVertexId, res["a"]["id"].ToString());
                    ////Assert.IsTrue(vadasVertexId.Equals(res["b"]["id"].ToString()) || joshVertexId.Equals(res["b"]["id"].ToString()));
                }
            }
        }

        /// <summary>
        /// Port of the g_VX1X_asXaX_outXknowsX_asXbX_selectXa_bX_byXnameX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V(v1Id).as('a').out('knows').as('b').select('a','b').by('name')", "v1Id", v1Id
        /// </summary>
        /// <remarks>
        /// This fails because Select for more than 1 key is not yet implemented.
        /// WorkItem to track this: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/37285
        /// </remarks>
        [TestMethod]
        [Ignore]
        public void HasVertexIdAsAOutKnowsAsBSelectABByName()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                string markoVertexId = this.ConvertToVertexId(graphCommand, "marko");
                ////string vadasVertexId = this.ConvertToVertexId(graphCommand, "vadas");
                ////string joshVertexId = this.ConvertToVertexId(graphCommand, "josh");

                graphCommand.OutputFormat = OutputFormat.GraphSON;
                var traversal = graphCommand.g().V()
                    .HasId(markoVertexId).As("a")
                    .Out("knows").As("b")
                    .Select("a", "b").By("name");

                var result = traversal.Next();
                dynamic dynamicResult = JsonConvert.DeserializeObject<dynamic>(result.FirstOrDefault());

                Assert.AreEqual(2, dynamicResult.Count);
                foreach (var res in dynamicResult)
                {
                    Assert.AreEqual(2, res.Count);
                    // Skipping this validation until we can fix the multi-key select.
                    ////Assert.AreEqual("marko", res["a"]);
                    ////Assert.IsTrue("vadas".Equals(res["b"]["name"].ToString()) || "josh".Equals(res["b"]["name"].ToString()));
                }
            }
        }

        /// <summary>
        /// Port of the g_VX1X_asXaX_outXknowsX_asXbX_selectXaX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V(v1Id).as('a').out('knows').as('b').select('a')", "v1Id", v1Id
        /// </summary>
        [TestMethod]
        public void HasVertexIdAsAOutKnowsAsBSelectA()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                string markoVertexId = this.ConvertToVertexId(graphCommand, "marko");

                graphCommand.OutputFormat = OutputFormat.GraphSON;
                var traversal = graphCommand.g().V()
                    .HasId(markoVertexId).As("a")
                    .Out("knows").As("b")
                    .Select("a");

                var result = traversal.Next();
                dynamic dynamicResult = JsonConvert.DeserializeObject<dynamic>(result.FirstOrDefault());

                Assert.AreEqual(2, dynamicResult.Count);
                foreach (var res in dynamicResult)
                {
                    Assert.AreEqual(markoVertexId, res["id"].ToString());
                }
            }
        }

        /// <summary>
        /// Port of the g_VX1X_asXaX_outXknowsX_asXbX_selectXaX_byXnameX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V(v1Id).as('a').out('knows').as('b').select('a').by('name')", "v1Id", v1Id
        /// </summary>
        /// <remarks>
        /// This test fails because By Modulating for Select is currently not supported.
        /// "Unable to cast object of type 'Microsoft.Azure.Graph.GremlinSelectOp' to type 'Microsoft.Azure.Graph.IGremlinByModulating'."
        /// WorkItem: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/37417
        /// </remarks>
        [TestMethod]
        [Ignore]
        public void HasVertexIdAsAOutKnowsAsBSelectAByName()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                string markoVertexId = this.ConvertToVertexId(graphCommand, "marko");

                graphCommand.OutputFormat = OutputFormat.GraphSON;
                var traversal = graphCommand.g().V()
                    .HasId(markoVertexId).As("a")
                    .Out("knows").As("b")
                    .Select("a").By("name");

                var result = traversal.Next();
                dynamic dynamicResult = JsonConvert.DeserializeObject<dynamic>(result.FirstOrDefault());

                Assert.AreEqual(2, dynamicResult.Count);
                foreach (var res in dynamicResult)
                {
                    Assert.AreEqual("marko", res["name"]);
                }
            }
        }

        /// <summary>
        /// Port of the g_V_asXaX_out_asXbX_selectXa_bX_byXnameX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V.as('a').out.as('b').select('a','b').by('name')"
        /// </summary>
        /// <remarks>
        /// This fails because Select for more than 1 key is not yet implemented.
        /// WorkItem to track this: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/37285
        /// This test fails because By Modulating for Select is currently not supported.
        /// "Unable to cast object of type 'Microsoft.Azure.Graph.GremlinSelectOp' to type 'Microsoft.Azure.Graph.IGremlinByModulating'."
        /// WorkItem: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/37417
        /// </remarks>
        [TestMethod]
        [Ignore]
        public void VerticesAsAOutAsBSelectABByName()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                graphCommand.OutputFormat = OutputFormat.GraphSON;
                var traversal = graphCommand.g().V()
                    .As("a")
                    .Out().As("b")
                    .Select("a", "b").By("name");

                var result = traversal.Next();
                dynamic dynamicResult = JsonConvert.DeserializeObject<dynamic>(result.FirstOrDefault());
                AssertCommonA(dynamicResult);
            }
        }

        /// <summary>
        /// Port of the g_V_asXaX_out_aggregateXxX_asXbX_selectXa_bX_byXnameX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V.as('a').out.aggregate('x').as('b').select('a','b').by('name')"
        /// </summary>
        /// <remarks>
        /// This fails because Select for more than 1 key is not yet implemented.
        /// WorkItem to track this: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/37285
        /// This test fails because By Modulating for Select is currently not supported.
        /// "Unable to cast object of type 'Microsoft.Azure.Graph.GremlinSelectOp' to type 'Microsoft.Azure.Graph.IGremlinByModulating'."
        /// WorkItem: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/37417
        /// </remarks>
        [TestMethod]
        [Ignore]
        public void VerticesAsAOutAggregateXAsBSelectABByName()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                graphCommand.OutputFormat = OutputFormat.GraphSON;
                var traversal = graphCommand.g().V()
                    .As("a")
                    .Out().Aggregate("x").As("b")
                    .Select("a", "b").By("name");

                var result = traversal.Next();
                dynamic dynamicResult = JsonConvert.DeserializeObject<dynamic>(result.FirstOrDefault());
                AssertCommonA(dynamicResult);
            }
        }

        /// <summary>
        /// Port of the g_V_asXaX_name_order_asXbX_selectXa_bX_byXnameX_by_XitX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V().as('a').name.order().as('b').select('a','b').by('name').by"
        /// </summary>
        /// <remarks>
        /// This fails because Select for more than 1 key is not yet implemented.
        /// WorkItem to track this: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/37285
        /// This test fails because By Modulating for Select is currently not supported.
        /// "Unable to cast object of type 'Microsoft.Azure.Graph.GremlinSelectOp' to type 'Microsoft.Azure.Graph.IGremlinByModulating'."
        /// WorkItem: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/37417
        /// </remarks>
        [TestMethod]
        [Ignore]
        public void VerticesAsAValuesNameOrderAsBSelectABByNameBy()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                graphCommand.OutputFormat = OutputFormat.GraphSON;
                var traversal = graphCommand.g().V()
                    .As("a")
                    .Values("name")
                    .Order().As("b").
                        Select("a", "b").By("name")
                    .By();

                var result = traversal.Next();
                dynamic dynamicResult = JsonConvert.DeserializeObject<dynamic>(result.FirstOrDefault());
                List<string> expected = new List<string>
                {
                    "a,marko;b,marko",
                    "a,vadas;b,vadas",
                    "a,josh;b,josh",
                    "a,ripple;b,ripple",
                    "a,lop;b,lop",
                    "a,peter;b,peter"
                };

                CheckUnOrderedResults(expected, dynamicResult);
            }
        }

        // g_V_hasXname_gremlinX_inEXusesX_order_byXskill_incrX_asXaX_outV_asXbX_selectXa_bX_byXskillX_byXnameX makes use of CREW data set, hence we're skipping it for now.

        /// <summary>
        /// Port of the g_V_hasXname_isXmarkoXX_asXaX_selectXaX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V.has('name',__.is('marko')).as('a').select('a')"
        /// </summary>
        /// <remarks>
        /// This test fails because Has Property Traversal doesn't seem to be supported yet.
        /// \development\euler\product\microsoft.azure.graph\graphview\gremlintranslation2\filter\gremlinhasop.cs Line 118.
        /// GremlinHasType.HasKeyTraversal is not implemented
        /// WorkItem to track this: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/36516
        /// </remarks>
        [TestMethod]
        [Ignore]
        public void HasNameIsMarkoAsASelectA()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                graphCommand.OutputFormat = OutputFormat.GraphSON;
                var traversal = graphCommand.g().V()
                    .Has("name", GraphTraversal2.__().Is("marko"))
                    .As("a").Select("a");

                var result = traversal.Next();
                dynamic dynamicResult = JsonConvert.DeserializeObject<dynamic>(result.FirstOrDefault());
                Assert.AreEqual(1, dynamicResult);
                Assert.AreEqual("marko", dynamicResult.FirstOrDefault()["name"]);
            }
        }

        /// <summary>
        /// Port of the g_V_label_groupCount_asXxX_selectXxX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V().label().groupCount().as('x').select('x')"
        /// </summary>
        [TestMethod]
        public void VerticesLabelGroupCountAsXSelectX()
        {
            //using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            //{
            //    var traversal = graphCommand.g().V().Label().GroupCount().As("x").Select("x");

            //    var result = traversal.Next();
            //    var groupCountMap = GetGroupCountMap(result);

            //    Assert.AreEqual(2, groupCountMap.Count);
            //    Assert.IsTrue(groupCountMap.ContainsKey("person"));
            //    Assert.IsTrue(groupCountMap.ContainsKey("software"));
            //    Assert.AreEqual(2, groupCountMap["software"]);
            //    Assert.AreEqual(4, groupCountMap["person"]);
            //}
        }

        /// <summary>
        /// Port of the g_V_hasLabelXpersonX_asXpX_mapXbothE_label_groupCountX_asXrX_selectXp_rX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V.hasLabel('person').as('p').map(__.bothE.label.groupCount()).as('r').select('p','r')"
        /// </summary>
        /// <remarks>
        /// This fails because Select for more than 1 key is not yet implemented.
        /// WorkItem to track this: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/37285
        /// </remarks>
        [TestMethod]
        [Ignore]
        public void HasLabelPersonAsPMapBothELabelGroupCountAsRSelectPR()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                graphCommand.OutputFormat = OutputFormat.GraphSON;
                var traversal = graphCommand.g().V()
                    .HasLabel("person").As("p")
                    .Map(GraphTraversal2.__().BothE().Label().GroupCount()).As("r").Select("p", "r");

                var result = traversal.Next();
                dynamic dynamicResult = JsonConvert.DeserializeObject<dynamic>(result.FirstOrDefault());

                ////for (int i = 0; i < 4; i++)
                ////{
                //      Skipping asserts until we fix the above bug.
                ////}
            }
        }

        /// <summary>
        /// Port of the g_V_chooseXoutE_count_isX0X__asXaX__asXbXX_chooseXselectXaX__selectXaX__selectXbXX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V.choose(__.outE().count().is(0L), __.as('a'), __.as('b')).choose(select('a'),select('a'),select('b'))"
        /// </summary>
        /// This fails because Choose is not implemented.
        /// \Development\Euler\Product\Microsoft.Azure.Graph\GraphView\GremlinTranslation2\branch\GremlinChooseOp.cs Line 45, in GetContext().
        /// WorkItem to track this: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/36531
        [TestMethod]
        [Ignore]
        public void ChooseOutECountIs0AsAsBChooseSelectASelectASelectB()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                graphCommand.OutputFormat = OutputFormat.GraphSON;
                var traversal = graphCommand.g().V().Choose(
                                                        GraphTraversal2.__().OutE().Count().Is(0L),
                                                        GraphTraversal2.__().As("a"),
                                                        GraphTraversal2.__().As("b"))
                                                    .Choose(
                                                        GraphTraversal2.__().Select("a"),
                                                        GraphTraversal2.__().Select("a"),
                                                        GraphTraversal2.__().Select("b"));

                var result = traversal.Next();
                dynamic dynamicResult = JsonConvert.DeserializeObject<dynamic>(result.FirstOrDefault());

                // Skipping asserts until we fix the above bug.
            }
        }

        /// <summary>
        /// Port of the g_VX1X_asXhereX_out_selectXhereX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V(v1Id).as('here').out.select('here')", "v1Id", v1Id
        /// </summary>
        [TestMethod]
        public void HasVertexIdAsHereOutSelectHere()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                string vertexId1 = this.ConvertToVertexId(graphCommand, "marko");

                graphCommand.OutputFormat = OutputFormat.GraphSON;
                var traversal = graphCommand.g().V().HasId(vertexId1).As("here").Out().Select("here");

                var result = traversal.Next();
                dynamic dynamicResult = JsonConvert.DeserializeObject<dynamic>(result.FirstOrDefault());

                Assert.AreEqual(3, dynamicResult.Count);
                foreach (var res in dynamicResult)
                {
                    Assert.AreEqual("marko", res["properties"]["name"][0]["value"].ToString());
                }
            }
        }

        /// <summary>
        /// Port of the g_VX4X_out_asXhereX_hasXlang_javaX_selectXhereX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V(v4Id).out.as('here').has('lang', 'java').select('here')", "v4Id", v4Id
        /// </summary>
        [TestMethod]
        public void HasVertexIdOutAsHereHasLangJavaSelectHere()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                string vertexId1 = this.ConvertToVertexId(graphCommand, "josh");

                graphCommand.OutputFormat = OutputFormat.GraphSON;
                var traversal = graphCommand.g().V().HasId(vertexId1).Out().As("here").Has("lang", "java").Select("here");

                var result = traversal.Next();
                dynamic dynamicResult = JsonConvert.DeserializeObject<dynamic>(result.FirstOrDefault());

                Assert.AreEqual(2, dynamicResult.Count);
                foreach (var res in dynamicResult)
                {
                    Assert.AreEqual("java", res["properties"]["lang"][0]["value"].ToString());
                    string propertiesNameVal = res["properties"]["name"][0]["value"].ToString();
                    Assert.IsTrue(string.Equals("ripple", propertiesNameVal) || string.Equals("lop", propertiesNameVal));
                }
            }
        }

        /// <summary>
        /// Port of the g_VX1X_outE_asXhereX_inV_hasXname_vadasX_selectXhereX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V(v1Id).outE.as('here').inV.has('name', 'vadas').select('here')", "v1Id", v1Id
        /// </summary>
        [TestMethod]
        public void HasVertexIdOutEAsHereInVHasNameVadasSelectHere()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                string vertexId1 = this.ConvertToVertexId(graphCommand, "marko");
                string vertexId2 = this.ConvertToVertexId(graphCommand, "vadas");

                graphCommand.OutputFormat = OutputFormat.GraphSON;
                var traversal = graphCommand.g().V().HasId(vertexId1).OutE().As("here").InV().Has("name", "vadas").Select("here");
                var result = traversal.Next();
                dynamic dynamicResult = JsonConvert.DeserializeObject<dynamic>(result.FirstOrDefault());

                Assert.AreEqual(1, dynamicResult.Count);
                var edge = dynamicResult[0];
                Assert.AreEqual("knows", edge["label"].ToString());
                Assert.AreEqual(vertexId2, edge["inV"].ToString());
                Assert.AreEqual(vertexId1, edge["outV"].ToString());
                Assert.AreEqual("0.5", edge["properties"]["weight"].ToString());
            }
        }

        /// <summary>
        /// Port of the g_VX4X_out_asXhereX_hasXlang_javaX_selectXhereX_name UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V(v4Id).out.as('here').has('lang', 'java').select('here').name", "v4Id", v4Id
        /// </summary>
        [TestMethod]
        public void HasVertexIdOutAsHereHasLangJavaSelectHereName()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                string vertexId1 = this.ConvertToVertexId(graphCommand, "josh");
                var traversal = graphCommand.g().V().HasId(vertexId1).Out().As("here").Has("lang", "java").Select("here").Values("name");

                var result = traversal.Next();
                Assert.AreEqual(2, result.Count);
                Assert.IsTrue(result.Contains("ripple"));
                Assert.IsTrue(result.Contains("lop"));
            }
        }

        /// <summary>
        /// Port of the g_VX1X_outEXknowsX_hasXweight_1X_asXhereX_inV_hasXname_joshX_selectXhereX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V(v1Id).outE('knows').has('weight', 1.0d).as('here').inV.has('name', 'josh').select('here')", "v1Id", v1Id
        /// </summary>
        [TestMethod]
        public void HasVertexIdOutEKnowsHasWeight1AsHereInVHasNameJoshSelectHere()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                string vertexId1 = this.ConvertToVertexId(graphCommand, "marko");

                graphCommand.OutputFormat = OutputFormat.GraphSON;
                var traversal = graphCommand.g().V().HasId(vertexId1).OutE("knows").Has("weight", 1.0d).As("here").InV().Has("name", "josh").Select("here");

                var result = traversal.Next();
                dynamic dynamicResult = JsonConvert.DeserializeObject<dynamic>(result.FirstOrDefault());
                AssertCommonB(dynamicResult);
            }
        }

        /// <summary>
        /// Port of the g_VX1X_outEXknowsX_asXhereX_hasXweight_1X_asXfakeX_inV_hasXname_joshX_selectXhereX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V(v1Id).outE('knows').as('here').has('weight', 1.0d).as('fake').inV.has('name', 'josh').select('here')", "v1Id", v1Id)
        /// </summary>
        [TestMethod]
        public void HasVertexIdOutEKnowsAsHereHasWeight1AsFakeInVHasNameJoshSelectHere()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                string vertexId1 = this.ConvertToVertexId(graphCommand, "marko");

                graphCommand.OutputFormat = OutputFormat.GraphSON;
                var traversal = graphCommand.g().V().HasId(vertexId1).OutE("knows").As("here").Has("weight", 1.0d).As("fake").InV().Has("name", "josh").Select("here");

                var result = traversal.Next();
                dynamic dynamicResult = JsonConvert.DeserializeObject<dynamic>(result.FirstOrDefault());
                AssertCommonB(dynamicResult);
            }
        }

        /// <summary>
        /// Port of the g_V_asXhereXout_name_selectXhereX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V().as('here').out.name.select('here')"
        /// </summary>
        [TestMethod]
        public void VerticesAsHereOutValuesNameSelectHere()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                var traversal = graphCommand.g().V().As("here").Out().Values("name").Select("here");

                // NOTE: actual tests for complete vertice, but here we just validate that the names are correct. We can do so since the names are unique.
                var result = traversal.Values("name").Next();
                var expectedResult = new List<string> { "marko", "marko", "marko", "josh", "josh", "peter" };
                CheckUnOrderedResults(expectedResult, result);
            }
        }

        /// <summary>
        /// Port of the g_V_outXcreatedX_unionXasXprojectX_inXcreatedX_hasXname_markoX_selectXprojectX__asXprojectX_inXcreatedX_inXknowsX_hasXname_markoX_selectXprojectXX_groupCount_byXnameX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V.out('created')
        ///                         .union(__.as('project').in('created').has('name', 'marko').select('project'),
        ///                         __.as('project').in('created').in('knows').has('name', 'marko').select('project')).groupCount().by('name')"
        /// </summary>
        [TestMethod]
        public void VerticesOutCreatedUnionAsProjectInCreatedHasNameMarkoSelectProjectAsProjectInCreatedInKnowsHasNameMarkoSelectProjectGroupCountByName()
        {
            //using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            //{
            //    var traversal = graphCommand.g().V().Out("created")
            //        .Union(GraphTraversal2.__().As("project").In("created").Has("name", "marko").Select("project"),
            //               GraphTraversal2.__().As("project").In("created").In("knows").Has("name", "marko").Select("project")).GroupCount().By("name");

            //    var result = traversal.Next();
            //    var groupCountMap = GetGroupCountMap(result);

            //    Assert.AreEqual(2, groupCountMap.Count);
            //    Assert.AreEqual(6, groupCountMap["lop"]);
            //    Assert.AreEqual(1, groupCountMap["ripple"]);
            //}
        }

        /// <summary>
        /// Port of the g_V_asXaX_hasXname_markoX_asXbX_asXcX_selectXa_b_cX_by_byXnameX_byXageX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V.as('a').has('name', 'marko').as('b').as('c').select('a','b','c').by().by('name').by('age')"
        /// </summary>
        /// <remarks>
        /// This fails because Select for more than 1 key is not yet implemented.
        /// WorkItem to track this: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/37285
        /// </remarks>
        [TestMethod]
        [Ignore]
        public void VerticesAsAHasNameMarkoAsBAsCSelectABCByByNameByAge()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                graphCommand.OutputFormat = OutputFormat.GraphSON;
                var traversal = graphCommand.g().V().As("a").Has("name", "marko").As("b").As("c").Select("a", "b", "c").By().By("name").By("age");

                var result = traversal.Next();

                // TODO: Implement this once we support multi-key selects.

                ////final Map< String, Object > map = traversal.next();
                ////assertEquals(3, map.size());
                ////assertEquals(g.V(convertToVertexId("marko")).next(), map.get("a"));
                ////assertEquals("marko", map.get("b"));
                ////assertEquals(29, map.get("c"));
                ////assertFalse(traversal.hasNext());
            }
        }

        /// <summary>
        /// Port of the g_V_hasLabelXsoftwareX_asXnameX_asXlanguageX_asXcreatorsX_selectXname_language_creatorsX_byXnameX_byXlangX_byXinXcreatedX_name_fold_orderXlocalXX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V.hasLabel('software').as('name').as('language').as('creators').select('name','language','creators').by('name').by('lang').
        ///                         by(__.in('created').values('name').fold().order(local))"
        /// </summary>
        /// <remarks>
        /// Two issues with this test:
        /// 1. This fails because Select for more than 1 key is not yet implemented.
        /// WorkItem to track this: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/37285
        /// 2. Order with Scope isn't implemented.
        /// WorkItem: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/37435
        /// </remarks>
        [TestMethod]
        [Ignore]
        public void HasLabelSoftwareAsNameAsLanguageAsCreatorsSelectNameLanguageCreatorsByNameByLangByInCreatedValuesNameFoldOrderLocal()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                graphCommand.OutputFormat = OutputFormat.GraphSON;
                var traversal = graphCommand.g().V().HasLabel("software").As("name").As("language").As("creators").Select("name", "language", "creators").By("name").By("lang").
                    By(GraphTraversal2.__().In("created").Values("name").Fold().Order(/*local*/));

                // TODO: Implement this once above bugs are fixed.

                ////for (int i = 0; i < 2; i++)
                ////{
                ////    assertTrue(traversal.hasNext());
                ////    final Map< String, Object > map = traversal.next();
                ////    assertEquals(3, map.size());
                ////    final List< String > creators = (List<String>)map.get("creators");
                ////    final boolean isLop = "lop".equals(map.get("name")) && "java".equals(map.get("language")) &&
                ////            creators.size() == 3 && creators.get(0).equals("josh") && creators.get(1).equals("marko") && creators.get(2).equals("peter");
                ////    final boolean isRipple = "ripple".equals(map.get("name")) && "java".equals(map.get("language")) &&
                ////            creators.size() == 1 && creators.get(0).equals("josh");
                ////    assertTrue(isLop ^ isRipple);
                ////}
            }
        }

        /// <summary>
        /// Port of the g_V_selectXaX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V." + (null == pop ? "select('a')" : "select(${pop}, 'a')"
        /// </summary>
        /// <remarks>
        /// Test needs to be fixed because Select of an unknown key throws exception instead of returning an empty collection.
        /// WorkItem: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/37442
        /// </remarks>
        [TestMethod]
        [Ignore]
        public void VerticesSelectA()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                GremlinKeyword.Pop?[] pops = { null, GremlinKeyword.Pop.Default, GremlinKeyword.Pop.first, GremlinKeyword.Pop.last };
                foreach (var pop in pops)
                {
                    var root = graphCommand.g().V();
                    var traversal = (!pop.HasValue) ? root.Select("a") : root.Select(pop.Value, "a");

                    var result = traversal.Next();
                }
            }
        }


        // NOTE: g_V_selectXa_bX, g_V_valueMap_selectXpop_aX, g_V_valueMap_selectXpop_a_bX UTs also have the the same issue as the above method.
        // Hence I'm not adding them right now.


        /// <summary>
        /// Port of the g_V_untilXout_outX_repeatXin_asXaXX_selectXaX_byXtailXlocalX_nameX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V.until(__.out.out).repeat(__.in.as('a')).select('a').by(tail(local).name)"
        /// </summary>
        /// <remarks>
        /// Bug: Tail with Scope param not implemented.
        /// WorkItem: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/37443
        /// </remarks>
        [TestMethod]
        [Ignore]
        public void VerticesUntilOutOutRepeatInAsASelectAByTailLocalName()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                var traversal = graphCommand.g().V().Until(GraphTraversal2.__().Out().Out()).Repeat(GraphTraversal2.__().In().As("a")).Select("a").By(GraphTraversal2.__().Tail(/*local*/).Values("name"));

                // TODO: Implement the Asserts once the above bugs are fixed.
            }
        }

        /// <summary>
        /// Port of the g_V_untilXout_outX_repeatXin_asXaX_in_asXbXX_selectXa_bX_byXnameX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V.until(__.out.out).repeat(__.in.as('a').in.as('b')).select('a','b').by('name')"
        /// This fails because Select for more than 1 key is not yet implemented.
        /// WorkItem to track this: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/37285
        /// </summary>
        [TestMethod]
        [Ignore]
        public void VerticesUntilOutOutRepeatInAsAInAsBSelectABByName()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                var traversal = graphCommand.g().V().Until(GraphTraversal2.__().Out().Out()).Repeat(GraphTraversal2.__().In().As("a").In().As("b")).Select("a", "b").By("name");

                // Skipping asserts until we fix the above bug.

                ////Map<String, String> result = traversal.next();
                ////assertEquals(2, result.size());
                ////assertEquals("josh", result.get("a"));
                ////assertEquals("marko", result.get("b"));
                ////result = traversal.next();
                ////assertEquals(2, result.size());
                ////assertEquals("josh", result.get("a"));
                ////assertEquals("marko", result.get("b"));
            }
        }

        /// <summary>
        /// Port of the g_V_asXaX_whereXoutXknowsXX_selectXaX_byXnameX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V().as('a').where(out('knows')).select('a')"
        /// </summary>
        [TestMethod]
        public void VerticesAsAWhereOutKnowsSelectA()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                graphCommand.OutputFormat = OutputFormat.GraphSON;
                var traversal = graphCommand.g().V().As("a").Where(GraphTraversal2.__().Out("knows")).Select("a");

                var result = traversal.Next();
                dynamic dynamicResult = JsonConvert.DeserializeObject<dynamic>(result.FirstOrDefault());

                Assert.AreEqual(1, dynamicResult.Count);
                var vertex = dynamicResult[0];
                Assert.AreEqual("marko", vertex["properties"]["name"][0]["value"].ToString());
            }
        }

        /// <summary>
        /// Port of the g_V_outE_weight_groupCount_selectXkeysX_unfold UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V.outE.weight.groupCount.select(keys).unfold"
        /// </summary>
        /// <remarks>
        /// Ignoring this test as it relies on a java implementation specific object: "key" of a "map".
        /// </remarks>
        [TestMethod]
        [Ignore]
        public void VerticesOutEValuesWeightGroupCountSelectKeys()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                ////var traversal = graphCommand.g().V().OutE().Values("weight").GroupCount().Select(keys).Unfold();
            }
        }

        // NOTE: g_V_outE_weight_groupCount_unfold_selectXkeysX_unfold, get_g_V_asXa_bX_out_asXcX_path_selectXkeysX porting is skipped for the same reason as above.

        /// <summary>
        /// Port of the g_V_outE_weight_groupCount_selectXvaluesX_unfold UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V.outE.weight.groupCount.select(values).unfold"
        /// </summary>
        /// <remarks>
        /// Ignoring this test as it relies on a java implementation specific object: "values" of a "map".
        /// </remarks>
        [TestMethod]
        [Ignore]
        public void VerticesOutEValuesWeightGroupCountSelectValues()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                ////var traversal = graphCommand.g().V().OutE().Values("weight").GroupCount().Select(values).Unfold();
            }
        }

        // NOTE: g_V_outE_weight_groupCount_unfold_selectXvaluesX_unfold,g_V_outE_weight_groupCount_selectXvaluesX_unfold_groupCount_selectXvaluesX_unfold
        //porting is skipped for the same reason as above.

        /// <summary>
        /// Port of the g_V_asXaX_outXknowsX_asXbX_localXselectXa_bX_byXnameXX UT from org/apache/tinkerpop/gremlin/process/traversal/step/map/SelectTest.java.
        /// Equivalent gremlin: "g.V.as('a').out('knows').as('b').local(select('a', 'b').by('name'))"
        /// </summary>
        /// <remarks>
        /// This fails because Select for more than 1 key is not yet implemented.
        /// WorkItem to track this: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/37285
        /// This test fails because By Modulating for Select is currently not supported.
        /// "Unable to cast object of type 'Microsoft.Azure.Graph.GremlinSelectOp' to type 'Microsoft.Azure.Graph.IGremlinByModulating'."
        /// WorkItem: https://msdata.visualstudio.com/DocumentDB/_workitems/edit/37417
        /// </remarks>
        [TestMethod]
        [Ignore]
        public void VerticesAsAOutKnowsAsBLocalSelectABByName()
        {
            using (GraphViewCommand graphCommand = new GraphViewCommand(graphConnection))
            {
                var traversal = graphCommand.g().V().As("a").Out("knows").As("b").Local(GraphTraversal2.__().Select("a", "b").By("name"));

                // Skipping asserts until we fix the above bugs.
            }
        }

        private void AssertCommonA(dynamic traversalResult)
        {
            List<string> expected = new List<string>
            {
                "a,marko;b,lop",
                "a,marko;b,vadas",
                "a,marko;b,josh",
                "a,josh;b,ripple",
                "a,josh;b,lop",
                "a,peter;b,lop"
            };

            CheckUnOrderedResults(expected, traversalResult);
        }

        private void AssertCommonB(dynamic dynamicResult)
        {
            Assert.AreEqual(1, dynamicResult.Count);
            var edge = dynamicResult[0];
            Assert.AreEqual("knows", edge["label"].ToString());
            Assert.AreEqual(1.0D, double.Parse(edge["properties"]["weight"].ToString()));
        }
    }
}
