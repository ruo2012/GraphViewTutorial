﻿using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CSharp;

namespace GraphView
{
    public enum OutputFormat
    {
        Regular = 0,
        GraphSON
    }

    public class GraphTraversal2 : IEnumerable<string>
    {
        public class GraphTraversalIterator : IEnumerator<string>
        {
            private string currentRecord;
            private GraphViewExecutionOperator currentOperator;
            OutputFormat outputFormat;
            bool firstCall;

            internal GraphTraversalIterator(GraphViewExecutionOperator pCurrentOperator,OutputFormat outputFormat)
            {
                currentOperator = pCurrentOperator;
                this.outputFormat = outputFormat;
                this.firstCall = true;
            }

            public bool MoveNext()
            {
                if (currentOperator == null) return false;

                if (outputFormat == OutputFormat.GraphSON)
                {
                    RawRecord outputRec = null;
                    StringBuilder graphsonBuilder = new StringBuilder();
                    graphsonBuilder.Append("[");
                    bool firstEntry = true;
                    while ((outputRec = currentOperator.Next()) != null)
                    {
                        if (firstEntry)
                        {
                            firstEntry = false;
                        }
                        else
                        {
                            graphsonBuilder.Append(", ");
                        }
                        graphsonBuilder.Append(outputRec[0].ToGraphSON());
                    }
                    graphsonBuilder.Append("]");

                    if (firstEntry && !firstCall)     // No results are pulled from the execution operator
                    {
                        return false;
                    }
                    else
                    {
                        firstCall = false;
                        currentRecord = graphsonBuilder.ToString();
                        return true;
                    }
                }
                else
                {
                    RawRecord outputRec = null;
                    if ((outputRec = currentOperator.Next()) != null)
                    {
                        switch (outputFormat)
                        {
                            case OutputFormat.GraphSON:
                                currentRecord = outputRec[0].ToGraphSON();
                                break;
                            default:
                                currentRecord = outputRec[0].ToString();
                                break;
                        }
                        return currentRecord != null;
                    }
                    else return false;
                }
            }

            public void Reset()
            {
            }

            object IEnumerator.Current
            {
                get
                {
                    return currentRecord;
                }
            }

            public string Current
            {
                get
                {
                    return currentRecord;
                }
            }

            public void Dispose()
            {

            }
        }

        public IEnumerator<string> GetEnumerator()
        {
            var sqlScript = LastGremlinTranslationOp.ToSqlScript();
            var str = sqlScript.ToString();
            it = new GraphTraversalIterator(sqlScript.Batches[0].Compile(null, Connection), outputFormat);
            return it;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public string SqlScript { get; set; }
        private GraphTraversalIterator it;
        public GraphViewConnection Connection { get; set; }
        internal List<GremlinTranslationOperator> GremlinTranslationOpList { get; set; }
        internal GremlinTranslationOperator LastGremlinTranslationOp { set; get; }

        OutputFormat outputFormat;

        public GraphTraversal2()
        {
            GremlinTranslationOpList = new List<GremlinTranslationOperator>();
        }

        public GraphTraversal2(GraphViewConnection pConnection)
        {
            GremlinTranslationOpList = new List<GremlinTranslationOperator>();
            Connection = pConnection;
            outputFormat = OutputFormat.Regular;
        }

        public GraphTraversal2(GraphViewConnection connection, OutputFormat outputFormat)
        {
            GremlinTranslationOpList = new List<GremlinTranslationOperator>();
            Connection = connection;
            this.outputFormat = outputFormat;
        }

        public List<string> Next()
        {
            var sqlScript = LastGremlinTranslationOp.ToSqlScript();
            SqlScript = sqlScript.ToString();
            //Console.WriteLine(str);     // Added temporarily for debugging purpose.
            //Console.WriteLine();

            var op = sqlScript.Batches[0].Compile(null, Connection);
            var rawRecordResults = new List<RawRecord>();
            RawRecord outputRec = null;
            while ((outputRec = op.Next()) != null)
            {
                rawRecordResults.Add(outputRec);
            }

            List<string> results = new List<string>();

            switch (outputFormat)
            {

                case OutputFormat.GraphSON:
                    string result = "[";
                    bool firstEntry = true;
                    foreach (var record in rawRecordResults)
                    {
                        if (firstEntry)
                        {
                            firstEntry = false;
                        }
                        else
                        {
                            result += ", ";
                        }
                        FieldObject field = record[0];
                        result += field.ToGraphSON();
                    }
                    result += "]";
                    results.Add(result);
                    break;
                default:
                    foreach (var record in rawRecordResults)
                    {
                        FieldObject field = record[0];
                        results.Add(field.ToString());
                    }
                    break;
            }

            return results;
        }

        internal void InsertGremlinOperator(int index, GremlinTranslationOperator newGremlinTranslationOp)
        {
            if (index >= GremlinTranslationOpList.Count || index == 0) 
                throw new QueryCompilationException();
            GremlinTranslationOpList.Insert(index, newGremlinTranslationOp);
            newGremlinTranslationOp.InputOperator = GremlinTranslationOpList[index-1];
            if (index + 1 < GremlinTranslationOpList.Count())
            {
                GremlinTranslationOpList[index + 1].InputOperator = newGremlinTranslationOp;
            }
        }

        internal void AddGremlinOperator(GremlinTranslationOperator newGremlinTranslationOp)
        {
            if (LastGremlinTranslationOp is GremlinAndOp && (LastGremlinTranslationOp as GremlinAndOp).IsInfix)
            {
                (LastGremlinTranslationOp as GremlinAndOp).SecondTraversal.AddGremlinOperator(newGremlinTranslationOp);
            }
            else if (LastGremlinTranslationOp is GremlinOrOp && (LastGremlinTranslationOp as GremlinOrOp).IsInfix)
            {
                (LastGremlinTranslationOp as GremlinOrOp).SecondTraversal.AddGremlinOperator(newGremlinTranslationOp);
            }
            else
            {
                GremlinTranslationOpList.Add(newGremlinTranslationOp);
                if (LastGremlinTranslationOp == null)
                {
                    LastGremlinTranslationOp = newGremlinTranslationOp;
                }
                else
                {
                    newGremlinTranslationOp.InputOperator = LastGremlinTranslationOp;
                    LastGremlinTranslationOp = newGremlinTranslationOp;
                }
            }
        }

        internal GremlinTranslationOperator GetStartOp()
        {
            return GremlinTranslationOpList.Count == 0 ? null : GremlinTranslationOpList.First();
        }

        internal GremlinTranslationOperator GetEndOp()
        {
            return LastGremlinTranslationOp;
        }

        public GraphTraversal2 AddE()
        {
            AddGremlinOperator(new GremlinAddEOp());
            return this;
        }

        public GraphTraversal2 AddE(string edgeLabel)
        {
            AddGremlinOperator(new GremlinAddEOp(edgeLabel));
            return this;
        }

        public GraphTraversal2 AddV()
        {
            AddGremlinOperator(new GremlinAddVOp());
            return this;
        }

        public GraphTraversal2 AddV(params object[] propertyKeyValues)
        {
            AddGremlinOperator(new GremlinAddVOp(propertyKeyValues));
            return this;
        }

        public GraphTraversal2 AddV(string vertexLabel)
        {
            AddGremlinOperator(new GremlinAddVOp(vertexLabel));
            return this;
        }

        public GraphTraversal2 Aggregate(string sideEffectKey)
        {
            AddGremlinOperator(new GremlinAggregateOp(sideEffectKey));
            return this;
        }

        public GraphTraversal2 And(params GraphTraversal2[] andTraversals)
        {
            if (andTraversals.Length == 0)
            {
                //Infix And step
                GraphTraversal2 firstTraversal = GraphTraversal2.__();
                GraphTraversal2 sencondTraversal = GraphTraversal2.__();
                for (var i = 1; i < GremlinTranslationOpList.Count; i++)
                {
                    firstTraversal.AddGremlinOperator(GremlinTranslationOpList[i].Copy());
                }
                for (var i = 1; i < GremlinTranslationOpList.Count; i++)
                {
                    GremlinTranslationOpList.RemoveAt(i);
                }
                LastGremlinTranslationOp = GremlinTranslationOpList.First();
                GremlinAndOp newAndOp = new GremlinAndOp(firstTraversal, sencondTraversal);
                AddGremlinOperator(newAndOp);
            }
            else
            {
                AddGremlinOperator(new GremlinAndOp(andTraversals));
            }
            return this;
        }

        public GraphTraversal2 As(params string[] labels) {
            AddGremlinOperator(new GremlinAsOp(labels));
            return this;    
        }

        public GraphTraversal2 Barrier()
        {
            AddGremlinOperator(new GremlinBarrierOp());
            return this;
        }

        public GraphTraversal2 Barrier(int maxBarrierSize)
        {
            AddGremlinOperator(new GremlinBarrierOp(maxBarrierSize));
            return this;
        }

        public GraphTraversal2 Both(params string[] edgeLabels)
        {
            AddGremlinOperator(new GremlinBothOp(edgeLabels));
            return this;
        }

        public GraphTraversal2 BothE(params string[] edgeLabels)
        {
            AddGremlinOperator(new GremlinBothEOp(edgeLabels));
            return this;
        }

        public GraphTraversal2 BothV()
        {
            AddGremlinOperator(new GremlinBothVOp());
            return this;
        }

        public GraphTraversal2 By()
        {
            GetEndOp().ModulateBy();
            return this;
        }

        public GraphTraversal2 By(GremlinKeyword.Order order)
        {
            GetEndOp().ModulateBy(order);
            return this;
        }

        public GraphTraversal2 By(IComparer comparer)
        {
            GetEndOp().ModulateBy(comparer);
            return this;
        }

        public GraphTraversal2 By(GremlinKeyword.Column column)
        {
            GetEndOp().ModulateBy(column);
            return this;
        }

        public GraphTraversal2 By(GremlinKeyword.Column column, GremlinKeyword.Order order)
        {
            GetEndOp().ModulateBy(column, order);
            return this;
        }

        public GraphTraversal2 By(GremlinKeyword.Column column, IComparer comparer)
        {
            GetEndOp().ModulateBy(column, comparer);
            return this;
        }

        public GraphTraversal2 By(string key)
        {
            GetEndOp().ModulateBy(key);
            return this;
        }

        public GraphTraversal2 By(string key, GremlinKeyword.Order order)
        {
            GetEndOp().ModulateBy(key, order);
            return this;
        }

        public GraphTraversal2 By(string key, IComparer order)
        {
            GetEndOp().ModulateBy(key, order);
            return this;
        }

        public GraphTraversal2 By(GraphTraversal2 traversal)
        {
            GetEndOp().ModulateBy(traversal);
            return this;
        }

        public GraphTraversal2 By(GraphTraversal2 traversal, GremlinKeyword.Order order)
        {
            GetEndOp().ModulateBy(traversal, order);
            return this;
        }

        public GraphTraversal2 By(GraphTraversal2 traversal, IComparer order)
        {
            GetEndOp().ModulateBy(traversal, order);
            return this;
        }

        public GraphTraversal2 Cap(params string[] sideEffectKeys)
        {
            AddGremlinOperator(new GremlinCapOp(sideEffectKeys));
            return this;
        }

        public GraphTraversal2 Choose(Predicate choosePredicate, GraphTraversal2 trueChoice, GraphTraversal2 falseChoice)
        {
            AddGremlinOperator(new GremlinChooseOp(choosePredicate, trueChoice, falseChoice));
            return this;
        }

        public GraphTraversal2 Choose(GraphTraversal2 traversalPredicate, GraphTraversal2 trueChoice, GraphTraversal2 falseChoice)
        {
            AddGremlinOperator(new GremlinChooseOp(traversalPredicate, trueChoice, falseChoice));
            return this;
        }

        public GraphTraversal2 Choose(GraphTraversal2 choiceTraversal)
        {
            AddGremlinOperator(new GremlinChooseOp(choiceTraversal));
            return this;
        }

        public GraphTraversal2 Coalesce(params GraphTraversal2[] coalesceTraversals)
        {
            AddGremlinOperator(new GremlinCoalesceOp(coalesceTraversals));
            return this;
        }

        public GraphTraversal2 Coin(double probability)
        {
            AddGremlinOperator(new GremlinCoinOp(probability));
            return this;
        }

        public GraphTraversal2 Constant()
        {
            AddGremlinOperator(new GremlinConstantOp(new List<object>()));
            return this;
        }

        public GraphTraversal2 Constant(object value)
        {
            AddGremlinOperator(new GremlinConstantOp(value));
            return this;
        }

        public GraphTraversal2 Count()
        {
            AddGremlinOperator(new GremlinCountOp(GremlinKeyword.Scope.global));
            return this;
        }

        public GraphTraversal2 Count(GremlinKeyword.Scope scope)
        {
            if (scope == GremlinKeyword.Scope.global)
            {
                AddGremlinOperator(new GremlinCountOp(scope));
            }
            else
            {
                AddGremlinOperator(new GremlinCountOp(scope));
            }
            return this;
        }

        public GraphTraversal2 CyclicPath()
        {
            AddGremlinOperator(new GremlinCyclicPathOp());
            return this;
        }

        public GraphTraversal2 Dedup(GremlinKeyword.Scope scope, params string[] dedupLabels)
        {
            AddGremlinOperator(new GremlinDedupOp(scope, dedupLabels));
            return this;
        }

        public GraphTraversal2 Dedup(params string[] dedupLabels)
        {
            AddGremlinOperator(new GremlinDedupOp(GremlinKeyword.Scope.global, dedupLabels));
            return this;
        }

        public GraphTraversal2 Drop()
        {
            AddGremlinOperator(new GremlinDropOp());
            return this;
        }

        public GraphTraversal2 E()
        {
            AddGremlinOperator(new GremlinEOp());
            return this;
        }

        public GraphTraversal2 E(params object[] edgeIdsOrElements)
        {
            AddGremlinOperator(new GremlinEOp(edgeIdsOrElements));
            return this;
        }

        public GraphTraversal2 E(List<object> edgeIdsOrElements)
        {
            AddGremlinOperator(new GremlinEOp(edgeIdsOrElements));
            return this;
        }

        public GraphTraversal2 Emit()
        {
            if (GetEndOp() is GremlinRepeatOp)
            {
                (GetEndOp() as GremlinRepeatOp).IsEmit = true;
            }
            else
            {
                AddGremlinOperator(new GremlinRepeatOp());
                (GetEndOp() as GremlinRepeatOp).IsEmit = true;
                (GetEndOp() as GremlinRepeatOp).EmitContext = true;
            }
            return this;
        }

        public GraphTraversal2 Emit(Predicate emitPredicate)
        {
            if (GetEndOp() is GremlinRepeatOp)
            {
                (GetEndOp() as GremlinRepeatOp).IsEmit = true;
                (GetEndOp() as GremlinRepeatOp).EmitPredicate = emitPredicate;
            }
            else
            {
                AddGremlinOperator(new GremlinRepeatOp());
                (GetEndOp() as GremlinRepeatOp).IsEmit = true;
                (GetEndOp() as GremlinRepeatOp).EmitPredicate = emitPredicate;
                (GetEndOp() as GremlinRepeatOp).EmitContext = true;
            }
            return this;
        }

        public GraphTraversal2 Emit(GraphTraversal2 emitTraversal)
        {
            if (GetEndOp() is GremlinRepeatOp)
            {
                (GetEndOp() as GremlinRepeatOp).IsEmit = true;
                (GetEndOp() as GremlinRepeatOp).EmitTraversal = emitTraversal;
            }
            else
            {
                AddGremlinOperator(new GremlinRepeatOp());
                (GetEndOp() as GremlinRepeatOp).IsEmit = true;
                (GetEndOp() as GremlinRepeatOp).EmitTraversal = emitTraversal;
                (GetEndOp() as GremlinRepeatOp).EmitContext = true;
            }
            return this;
        }

        public GraphTraversal2 FlatMap(GraphTraversal2 flatMapTraversal)
        {
            AddGremlinOperator(new GremlinFlatMapOp(flatMapTraversal));
            return this;
        }

        public GraphTraversal2 Fold()
        {
            AddGremlinOperator(new GremlinFoldOp());
            return this;
        }

        public GraphTraversal2 From(string fromGremlinTranslationOperatorLabel)
        {
            AddGremlinOperator(new GremlinFromOp(fromGremlinTranslationOperatorLabel));
            return this;
        }

        public GraphTraversal2 From(GraphTraversal2 fromVertexTraversal)
        {
            AddGremlinOperator(new GremlinFromOp(fromVertexTraversal));
            return this;
        }

        public GraphTraversal2 Group()
        {
            AddGremlinOperator(new GremlinGroupOp());
            return this;
        }

        public GraphTraversal2 Group(string sideEffectKey)
        {
            AddGremlinOperator(new GremlinGroupOp(sideEffectKey));
            return this;
        }

        public GraphTraversal2 GroupCount()
        {
            GremlinGroupOp newGroupOp = new GremlinGroupOp();
            newGroupOp.ProjectBy = __().Count();
            AddGremlinOperator(newGroupOp);
            return this;
        }

        public GraphTraversal2 GroupCount(string sideEffectKey)
        {
            GremlinGroupOp newGroupOp = new GremlinGroupOp(sideEffectKey);
            newGroupOp.ProjectBy = __().Count();
            AddGremlinOperator(newGroupOp);
            return this;
        }

        public GraphTraversal2 Has(string propertyKey)
        {
            AddGremlinOperator(new GremlinHasOp(GremlinHasType.HasProperty, propertyKey));
            return this;
        }

        public GraphTraversal2 HasNot(string propertyKey)
        {
            AddGremlinOperator(new GremlinHasOp(GremlinHasType.HasNotProperty, propertyKey));
            return this;
        }

        public GraphTraversal2 Has(string propertyKey, object predicateOrValue)
        {
            GremlinUtil.CheckIsValueOrPredicate(predicateOrValue);
            AddGremlinOperator(new GremlinHasOp(propertyKey, predicateOrValue));
            return this;
        }

        public GraphTraversal2 Has(string propertyKey, GraphTraversal2 propertyTraversal)
        {
            AddGremlinOperator(new GremlinHasOp(propertyKey, propertyTraversal));
            return this;
        }

        public GraphTraversal2 Has(string label, string propertyKey, object predicateOrValue)
        {
            GremlinUtil.CheckIsValueOrPredicate(predicateOrValue);
            AddGremlinOperator(new GremlinHasOp(label, propertyKey, predicateOrValue));
            return this;
        }

        public GraphTraversal2 HasId(params object[] valuesOrPredicates)
        {
            GremlinUtil.CheckIsValueOrPredicate(valuesOrPredicates);
            AddGremlinOperator(new GremlinHasOp(GremlinHasType.HasId, valuesOrPredicates));
            return this;
        }

        public GraphTraversal2 HasLabel(params object[] valuesOrPredicates)
        {
            GremlinUtil.CheckIsValueOrPredicate(valuesOrPredicates);
            AddGremlinOperator(new GremlinHasOp(GremlinHasType.HasLabel, valuesOrPredicates));
            return this;
        }

        public GraphTraversal2 HasKey(params string[] valuesOrPredicates)
        {
            AddGremlinOperator(new GremlinHasOp(GremlinHasType.HasKey, valuesOrPredicates));
            return this;
        }

        public GraphTraversal2 HasValue(params object[] valuesOrPredicates)
        {
            GremlinUtil.CheckIsValueOrPredicate(valuesOrPredicates);
            AddGremlinOperator(new GremlinHasOp(GremlinHasType.HasValue, valuesOrPredicates));
            return this;
        }

        public GraphTraversal2 Id()
        {
            AddGremlinOperator(new GremlinIdOp());
            return this;
        }

        public GraphTraversal2 Identity()
        {
            return this;
        }

        public GraphTraversal2 In(params string[] edgeLabels)
        {
            AddGremlinOperator(new GremlinInOp(edgeLabels));
            return this;
        }

        public GraphTraversal2 InE(params string[] edgeLabels)
        {
            AddGremlinOperator(new GremlinInEOp(edgeLabels));
            return this;
        }

        public GraphTraversal2 Inject(params object[] injections)
        {
            AddGremlinOperator(new GremlinInjectOp(injections));
            return this;
        }

        public GraphTraversal2 InV()
        {
            AddGremlinOperator(new GremlinInVOp());
            return this;
        }

        public GraphTraversal2 Is(object value)
        {
            AddGremlinOperator(new GremlinIsOp(value));
            return this;
        }

        public GraphTraversal2 Is(Predicate predicate)
        {
            AddGremlinOperator(new GremlinIsOp(predicate));
            return this;
        }

        public GraphTraversal2 Key()
        {
            AddGremlinOperator(new GremlinKeyOp());
            return this;
        }

        public GraphTraversal2 Label()
        {
            AddGremlinOperator(new GremlinValuesOp(GremlinKeyword.Label));
            return this;
        }

        public GraphTraversal2 Limit(int limit)
        {
            AddGremlinOperator(new GremlinRangeOp(0, limit, GremlinKeyword.Scope.global));
            return this;
        }

        public GraphTraversal2 Limit(GremlinKeyword.Scope scope, int limit)
        {
            AddGremlinOperator(new GremlinRangeOp(0, limit, scope));
            return this;
        }

        public GraphTraversal2 Local(GraphTraversal2 localTraversal)
        {
            AddGremlinOperator(new GremlinLocalOp(localTraversal));
            return this;
        }

        //public GraphTraversal2 loops()

        public GraphTraversal2 Map(GraphTraversal2 mapTraversal)
        {
            AddGremlinOperator(new GremlinMapOp(mapTraversal));
            return this;   
        }

        //public GraphTraversal2 mapKeys() //Deprecated
        //public GraphTraversal2 mapvalues() //Deprecated

        public GraphTraversal2 Match(params GraphTraversal2[] matchTraversals)
        {
            AddGremlinOperator(new GremlinMatchOp(matchTraversals));
            return this;
        }

        public GraphTraversal2 Max()
        {
            AddGremlinOperator(new GremlinMaxOp(GremlinKeyword.Scope.global));
            return this;
        }

        public GraphTraversal2 Max(GremlinKeyword.Scope scope)
        {
            AddGremlinOperator(new GremlinMaxOp(scope));
            return this;
        }

        public GraphTraversal2 Mean()
        {
            AddGremlinOperator(new GremlinMeanOp(GremlinKeyword.Scope.global));
            return this;
        }

        public GraphTraversal2 Mean(GremlinKeyword.Scope scope)
        {
            AddGremlinOperator(new GremlinMeanOp(scope));
            return this;
        }

        public GraphTraversal2 Min()
        {
            AddGremlinOperator(new GremlinMinOp(GremlinKeyword.Scope.global));
            return this;
        }

        public GraphTraversal2 Min(GremlinKeyword.Scope scope)
        {
            AddGremlinOperator(new GremlinMinOp(scope));
            return this;
        }

        public GraphTraversal2 Not(GraphTraversal2 notTraversal)
        {
           AddGremlinOperator(new GremlinNotOp(notTraversal));
            return this;
        }
        public GraphTraversal2 Option(object pickToken, GraphTraversal2 traversalOption)
        {
            if (LastGremlinTranslationOp is GremlinChooseOp)
            {
                (LastGremlinTranslationOp as GremlinChooseOp).OptionDict[pickToken] = traversalOption;
                return this;
            }
            else
            {
                throw new Exception("Option step only can follow by choose step.");
            }
        }

        public GraphTraversal2 Option(GraphTraversal2 tarversalOption)
        {
            throw new NotImplementedException();
        }

        public GraphTraversal2 Optional(GraphTraversal2 traversalOption)
        {
            AddGremlinOperator(new GremlinOptionalOp(traversalOption));
            return this;
        }

        public GraphTraversal2 Or(params GraphTraversal2[] orTraversals)
        {
            if (orTraversals.Length == 0)
            {
                //Infix And step
                GraphTraversal2 firstTraversal = GraphTraversal2.__();
                GraphTraversal2 sencondTraversal = GraphTraversal2.__();
                for (var i = 1; i < GremlinTranslationOpList.Count; i++)
                {
                    firstTraversal.AddGremlinOperator(GremlinTranslationOpList[i].Copy());
                }
                for (var i = 1; i < GremlinTranslationOpList.Count; i++)
                {
                    GremlinTranslationOpList.RemoveAt(i);
                }
                LastGremlinTranslationOp = GremlinTranslationOpList.First();
                GremlinOrOp newOrOp = new GremlinOrOp(firstTraversal, sencondTraversal);
                AddGremlinOperator(newOrOp);
            }
            else
            {
                AddGremlinOperator(new GremlinOrOp(orTraversals));
            }
            return this;
        }

        public GraphTraversal2 Order()
        {
            AddGremlinOperator(new GremlinOrderOp(GremlinKeyword.Scope.global));
            return this;
        }

        public GraphTraversal2 Order(GremlinKeyword.Scope scope)
        {
            AddGremlinOperator(new GremlinOrderOp(scope));
            return this;
        }

        public GraphTraversal2 OtherV()
        {
            AddGremlinOperator(new GremlinOtherVOp());
            return this;
        }

        public GraphTraversal2 Out(params string[] edgeLabels)
        {
            AddGremlinOperator(new GremlinOutOp(edgeLabels));
            return this;
        }

        public GraphTraversal2 OutE(params string[] edgeLabels)
        {
            AddGremlinOperator(new GremlinOutEOp(edgeLabels));
            return this;
        }

        public GraphTraversal2 OutV()
        {
            AddGremlinOperator(new GremlinOutVOp());
            return this;
        }

        public GraphTraversal2 Path()
        {
            AddGremlinOperator(new GremlinPathOp());
            return this;
        }

        public GraphTraversal2 Project(params string[] projectKeys)
        {
            AddGremlinOperator(new GremlinProjectOp(projectKeys));
            return this;
        }

        public GraphTraversal2 Properties(params string[] propertyKeys)
        {
            AddGremlinOperator(new GremlinPropertiesOp(propertyKeys));
            return this;
        }

        public GraphTraversal2 Property(string key, object value, params object[] keyValues)
        {
            return Property(GremlinKeyword.PropertyCardinality.single, key, value, keyValues);
        }

        public GraphTraversal2 Property(GremlinKeyword.PropertyCardinality cardinality, string key, object value,
            params object[] keyValues)
        {
            if (keyValues.Length % 2 != 0) throw new Exception("The parameter of property should be even");
            Dictionary<string, object> metaProperties = new Dictionary<string, object>();
            for (var i = 0; i < keyValues.Length; i += 2)
            {
                metaProperties[keyValues[i] as string] = keyValues[i + 1];
            }
            GremlinProperty property = new GremlinProperty(cardinality, key, value, metaProperties);
            AddGremlinOperator(new GremlinPropertyOp(property));
            return this;
        }

        public GraphTraversal2 PropertyMap(params string[] propertyKeys)
        {
            AddGremlinOperator(new GremlinPropertyMapOp(propertyKeys));
            return this;
        }

        public GraphTraversal2 Range(int low, int high)
        {
            AddGremlinOperator(new GremlinRangeOp(low, high, GremlinKeyword.Scope.global));
            return this;
        }

        public GraphTraversal2 Range(GremlinKeyword.Scope scope, int low, int high)
        {
            AddGremlinOperator(new GremlinRangeOp(low, high, scope));
            return this;
        }

        public GraphTraversal2 Repeat(GraphTraversal2 repeatTraversal)
        {
            if (GetEndOp() is GremlinRepeatOp)
            {
                (GetEndOp() as GremlinRepeatOp).RepeatTraversal = repeatTraversal;
            }
            else
            {
                AddGremlinOperator(new GremlinRepeatOp(repeatTraversal));
            }
            return this;
        }

        public GraphTraversal2 Sample(int amountToSample)
        {
            AddGremlinOperator(new GremlinSampleOp(GremlinKeyword.Scope.global, amountToSample));
            return this;
        }

        public GraphTraversal2 Sample(GremlinKeyword.Scope scope, int amountToSample)
        {
            AddGremlinOperator(new GremlinSampleOp(scope, amountToSample));
            return this;
        }

        //public GraphTraversal2 Select(Column column)
        //public GraphTraversal2 Select(Pop pop, string selectKey)

        public GraphTraversal2 Select(GremlinKeyword.Pop pop, params string[] selectKeys)
        {
            AddGremlinOperator(new GremlinSelectOp(pop, selectKeys));
            return this;
        }

        public GraphTraversal2 Select(params string[] selectKeys)
        {
            AddGremlinOperator(new GremlinSelectOp(selectKeys));
            return this;
        }


        public GraphTraversal2 SideEffect(GraphTraversal2 sideEffectTraversal)
        {
            AddGremlinOperator(new GremlinSideEffectOp(sideEffectTraversal));
            return this;    
        }

        public GraphTraversal2 SimplePath()
        {
            AddGremlinOperator(new GremlinSimplePathOp());
            return this;
        }

        public GraphTraversal2 Store(string sideEffectKey)
        {
            AddGremlinOperator(new GremlinStoreOp(sideEffectKey));
            return this;
        }

        public GraphTraversal2 Sum()
        {
            AddGremlinOperator(new GremlinSumOp(GremlinKeyword.Scope.global));
            return this;
        }

        public GraphTraversal2 Sum(GremlinKeyword.Scope scope)
        {
            AddGremlinOperator(new GremlinSumOp(scope));
            return this;
        }

        public GraphTraversal2 Tail()
        {
            AddGremlinOperator(new GremlinRangeOp(0, 1, GremlinKeyword.Scope.global, true));
            return this;
        }

        public GraphTraversal2 Tail(int limit)
        {
            AddGremlinOperator(new GremlinRangeOp(0, limit, GremlinKeyword.Scope.global, true));
            return this;
        }

        public GraphTraversal2 Tail(GremlinKeyword.Scope scope)
        {
            AddGremlinOperator(new GremlinRangeOp(0, 1, scope, true));
            return this;
        }

        public GraphTraversal2 Tail(GremlinKeyword.Scope scope, int limit)
        {
            AddGremlinOperator(new GremlinRangeOp(0, limit, scope, true));
            return this;
        }

        public GraphTraversal2 TimeLimit(long timeLimit)
        {
            throw new NotImplementedException();
        }

        public GraphTraversal2 Times(int maxLoops)
        {
            if (GetEndOp() is GremlinRepeatOp)
            {
                (GetEndOp() as GremlinRepeatOp).RepeatTimes = maxLoops;
            }
            else
            {
                AddGremlinOperator(new GremlinRepeatOp());
                (GetEndOp() as GremlinRepeatOp).RepeatTimes = maxLoops;
            }
            return this;
        }

        public GraphTraversal2 To(string toGremlinTranslationOperatorLabel)
        {
            AddGremlinOperator(new GremlinToOp(toGremlinTranslationOperatorLabel));
            return this;
        }

        public GraphTraversal2 To(GraphTraversal2 toVertex)
        {
            AddGremlinOperator(new GremlinToOp(toVertex));
            return this;
        }

        public GraphTraversal2 Tree()
        {
            AddGremlinOperator(new GremlinTreeOp());
            return this;
        }

        public GraphTraversal2 Tree(string sideEffectKey)
        {
            AddGremlinOperator(new GremlinTreeOp(sideEffectKey));
            return this;
        }

        public GraphTraversal2 Unfold()
        {
            AddGremlinOperator(new GremlinUnfoldOp());
            return this;
        }

        public GraphTraversal2 Union(params GraphTraversal2[] unionTraversals)
        {
            AddGremlinOperator(new GremlinUnionOp(unionTraversals));
            return this;
        }

        public GraphTraversal2 Until(Predicate untilPredicate)
        {
            if (GetEndOp() is GremlinRepeatOp)
            {
                (GetEndOp() as GremlinRepeatOp).TerminationPredicate = untilPredicate;
            }
            else
            {
                AddGremlinOperator(new GremlinRepeatOp());
                (GetEndOp() as GremlinRepeatOp).TerminationPredicate = untilPredicate;
                (GetEndOp() as GremlinRepeatOp).StartFromContext = true;
            }
            return this;
        }

        public GraphTraversal2 Until(GraphTraversal2 untilTraversal)
        {
            if (GetEndOp() is GremlinRepeatOp)
            {
                (GetEndOp() as GremlinRepeatOp).TerminationTraversal = untilTraversal;
            }
            else
            {
                AddGremlinOperator(new GremlinRepeatOp());
                (GetEndOp() as GremlinRepeatOp).TerminationTraversal = untilTraversal;
                (GetEndOp() as GremlinRepeatOp).StartFromContext = true;
            }
            return this;
        }

        public GraphTraversal2 V(params object[] vertexIdsOrElements)
        {
            AddGremlinOperator(new GremlinVOp(vertexIdsOrElements));
            return this;
        }

        public GraphTraversal2 V(List<object> vertexIdsOrElements)
        {
            AddGremlinOperator(new GremlinVOp(vertexIdsOrElements));
            return this;
        }

        public GraphTraversal2 Value()
        {
            AddGremlinOperator(new GremlinValueOp());
            return this;
        }

        public GraphTraversal2 ValueMap(params string[] propertyKeys)
        {
            AddGremlinOperator(new GremlinValueMapOp(false, propertyKeys));
            return this;
        }

        public GraphTraversal2 ValueMap(bool isIncludeTokens, params string[] propertyKeys)
        {
            AddGremlinOperator(new GremlinValueMapOp(isIncludeTokens, propertyKeys));
            return this;
        }

        public GraphTraversal2 Values(params string[] propertyKeys)
        {
            AddGremlinOperator(new GremlinValuesOp(propertyKeys));
            return this;
        }

        public GraphTraversal2 Where(Predicate predicate)
        {
            AddGremlinOperator(new GremlinWhereOp(predicate));
            return this;
        }

        public GraphTraversal2 Where(string startKey, Predicate predicate)
        {
            //AddGremlinOperator(new GremlinWhereOp(startKey, predicate));
            AddGremlinOperator(new GremlinWhereOp(GraphTraversal2.__().V().Has(startKey, predicate)));
            return this;
        }

        public GraphTraversal2 Where(GraphTraversal2 whereTraversal)
        {
            AddGremlinOperator(new GremlinWhereOp(whereTraversal));
            return this;
        }

        public static GraphTraversal2 __()
        {
            GraphTraversal2 newGraphTraversal = new GraphTraversal2();
            newGraphTraversal.AddGremlinOperator(new GremlinParentContextOp());
            return newGraphTraversal;
        }

        public List<object> ToList()
        {
            //TODO
            var str = LastGremlinTranslationOp.ToSqlScript().ToString();
            return new List<object>() {1};
        }

        public IEnumerable<string> EvalGremlinTraversal(string sCSCode)
        {
            return EvalGraphTraversal(ConvertGremlinToGraphTraversalCode(sCSCode));    
        }

        public string ConvertGremlinToGraphTraversalCode(string sCSCode)
        {
            sCSCode = sCSCode.Replace("\'", "\"");

            //repleace gremlin steps with uppercase
            foreach (var item in GremlinKeyword.GremlinStepToGraphTraversalDict)
            {
                string originStr = "." + item.Key + "(";
                string targetStr = "." + item.Value + "(";
                sCSCode = sCSCode.Replace(originStr, targetStr);
            }
            //repleace with GraphTraversal FunctionName
            foreach (var item in GremlinKeyword.GremlinMainStepToGraphTraversalDict)
            {
                sCSCode = sCSCode.Replace(item.Key, item.Value);
            }
            //repleace gremlin predicate with GraphTraversal predicate
            foreach (var item in GremlinKeyword.GremlinPredicateToGraphTraversalDict)
            {
                Regex r = new Regex("[^a-zA-Z](" + item.Key + ")\\(");
                if (r.IsMatch(sCSCode))
                {
                    var match = r.Match(sCSCode);
                    sCSCode = sCSCode.Replace(match.Groups[0].Value, match.Groups[0].Value[0] + item.Value + "(");
                }
            }

            //repeleace gremlin keyword
            foreach (var item in GremlinKeyword.GremlinKeywordToGraphTraversalDict)
            {
                RegexOptions ops = RegexOptions.Multiline;
                Regex r = new Regex("[^\"](" + item.Key + ")[^\"]", ops);
                if (r.IsMatch(sCSCode))
                {
                    var match = r.Match(sCSCode);
                    sCSCode = sCSCode.Replace(match.Groups[1].Value, item.Value);
                }
            }

            //replace gremlin array with C# array
            Regex arrayRegex = new Regex("[\\[]((\\s*?[\\\"|']\\w+[\\\"|']\\s*?[,]*?\\s*?)*)[\\]]", RegexOptions.Multiline);
            var matchtest = arrayRegex.Match(sCSCode);
            if (arrayRegex.IsMatch(sCSCode))
            {
                var matchs = arrayRegex.Matches(sCSCode);
                for (var i = 0; i < matchs.Count; i++)
                {
                    List<string> values = new List<string>();
                    for (var j = 0; j < matchs[i].Groups.Count; j++)
                    {
                        values.Add(matchs[i].Groups[j].Value);
                    }
                    sCSCode = sCSCode.Replace(matchs[i].Groups[0].Value, "new List<string>() {"+ matchs[i].Groups[1].Value + "}");
                    values.Clear();
                }
            }
            return sCSCode;
        }

        public IEnumerable<string> EvalGraphTraversal(string sCSCode)
        {
            CompilerParameters cp = new CompilerParameters();
            cp.ReferencedAssemblies.Add("GraphView.dll");
            cp.ReferencedAssemblies.Add("System.dll");
            cp.GenerateInMemory = true;

            StringBuilder sb = new StringBuilder("");
            sb.Append("using GraphView;\n");
            sb.Append("using System;\n");
            sb.Append("using System.Collections.Generic;\n");

            sb.Append("namespace GraphView { \n");
            sb.Append("public class Program { \n");
            sb.Append("public object Main() {\n");
            sb.Append("GraphViewConnection connection = new GraphViewConnection("+ getConnectionInfo() +");");
            sb.Append("GraphViewCommand graph = new GraphViewCommand(connection);\n");
            switch(outputFormat)
            {
                case OutputFormat.GraphSON:
                    sb.Append("graph.OutputFormat = OutputFormat.GraphSON;\r\n");
                    break;
            }
            sb.Append("return " + sCSCode + ";\n");
            sb.Append("}\n");
            sb.Append("}\n");
            sb.Append("}\n");

            CodeDomProvider icc = CodeDomProvider.CreateProvider("CSharp");
            CompilerResults cr = icc.CompileAssemblyFromSource(cp, sb.ToString());
            if (cr.Errors.Count > 0)
            {
                throw new Exception("ERROR: " + cr.Errors[0].ErrorText + "Error evaluating cs code");
            }

            System.Reflection.Assembly a = cr.CompiledAssembly;
            object o = a.CreateInstance("GraphView.Program");

            Type t = o.GetType();
            MethodInfo mi = t.GetMethod("Main");

            return (IEnumerable<string>)mi.Invoke(o, null);
        }

        private string addDoubleQuotes(string str)
        {
            return "\"" + str + "\"";
        }

        private string getConnectionInfo()
        {
            List<string> connectionList = new List<string>();
            connectionList.Add(addDoubleQuotes(Connection.DocDBUrl));
            connectionList.Add(addDoubleQuotes(Connection.DocDBPrimaryKey));
            connectionList.Add(addDoubleQuotes(Connection.DocDBDatabaseId));
            connectionList.Add(addDoubleQuotes(Connection.DocDBCollectionId));
            return string.Join(",", connectionList);
        }
    }
}


