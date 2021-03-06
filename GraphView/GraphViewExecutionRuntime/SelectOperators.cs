﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Newtonsoft.Json.Linq;

namespace GraphView
{
    internal class ConstantSourceOperator : GraphViewExecutionOperator
    {
        private RawRecord _constantSource;
        ContainerEnumerator sourceEnumerator;

        public RawRecord ConstantSource
        {
            get { return _constantSource; }
            set { _constantSource = value; this.Open(); }
        }

        public ContainerEnumerator SourceEnumerator
        {
            get { return sourceEnumerator; }
            set
            {
                sourceEnumerator = value;
                Open();
            }
        }

        public ConstantSourceOperator()
        {
            Open();
        }

        public override RawRecord Next()
        {
            if (sourceEnumerator != null)
            {
                if (sourceEnumerator.MoveNext())
                {
                    return sourceEnumerator.Current;
                }
                else
                {
                    Close();
                    return null;
                }
            }
            else
            {
                if (!State())
                    return null;

                Close();
                return _constantSource;
            }
        }

        public override void ResetState()
        {
            if (sourceEnumerator != null)
            {
                sourceEnumerator.Reset();
                Open();
            }
            else
            {
                Open();
            }
        }
    }

    internal class FetchNodeOperator2 : GraphViewExecutionOperator
    {
        private Queue<RawRecord> outputBuffer;
        private JsonQuery vertexQuery;
        private GraphViewConnection connection;

        private IEnumerator<RawRecord> verticesEnumerator;

        public FetchNodeOperator2(GraphViewConnection connection, JsonQuery vertexQuery)
        {
            Open();
            this.connection = connection;
            this.vertexQuery = vertexQuery;
            verticesEnumerator = connection.CreateDatabasePortal().GetVertices(vertexQuery);
        }

        public override RawRecord Next()
        {
            if (verticesEnumerator.MoveNext())
            {
                return verticesEnumerator.Current;
            }

            Close();
            return null;
        }

        public override void ResetState()
        {
            verticesEnumerator = connection.CreateDatabasePortal().GetVertices(vertexQuery);
            outputBuffer?.Clear();
            Open();
        }
    }


    /// <summary>
    /// The operator that takes a list of records as source vertexes and 
    /// traverses to their one-hop or multi-hop neighbors. One-hop neighbors
    /// are defined in the adjacency lists of the sources. Multi-hop
    /// vertices are defined by a recursive function that has a sub-query
    /// specifying a single hop from a vertex to another and a boolean fuction 
    /// controlling when the recursion terminates (in other words, # of hops).  
    /// 
    /// This operators emulates the nested-loop join algorithm.
    /// </summary>
    internal class TraversalOperator2 : GraphViewExecutionOperator
    {
        private int outputBufferSize;
        private int batchSize = 1100;
        private Queue<RawRecord> outputBuffer;
        private GraphViewConnection connection;
        private GraphViewExecutionOperator inputOp;
        
        // The index of the adjacency list in the record from which the traversal starts
        private int adjacencyListSinkIndex = -1;

        // The query that describes predicates on the sink vertices and its properties to return.
        // It is null if the sink vertex has no predicates and no properties other than sink vertex ID
        // are to be returned.  
        private JsonQuery sinkVertexQuery;

        // A list of index pairs, each specifying which field in the source record 
        // must match the field in the sink record. 
        // This list is not null when sink vertices have edges pointing back 
        // to the vertices other than the source vertices in the records by the input operator. 
        private List<Tuple<int, int>> matchingIndexes;

        public TraversalOperator2(
            GraphViewExecutionOperator inputOp,
            GraphViewConnection connection,
            int sinkIndex,
            JsonQuery sinkVertexQuery,
            List<Tuple<int, int>> matchingIndexes,
            int outputBufferSize = 1000)
        {
            Open();
            this.inputOp = inputOp;
            this.connection = connection;
            this.adjacencyListSinkIndex = sinkIndex;
            this.sinkVertexQuery = sinkVertexQuery;
            this.matchingIndexes = matchingIndexes;
            this.outputBufferSize = outputBufferSize;
        }

        public override RawRecord Next()
        {
            if (outputBuffer == null)
            {
                outputBuffer = new Queue<RawRecord>(outputBufferSize);
            }

            while (outputBuffer.Count < outputBufferSize && inputOp.State())
            {
                List<Tuple<RawRecord, string>> inputSequence = new List<Tuple<RawRecord, string>>(batchSize);

                // Loads a batch of source records
                for (int i = 0; i < batchSize && inputOp.State(); i++)
                {
                    RawRecord record = inputOp.Next();
                    if (record == null)
                    {
                        break;
                    }

                    inputSequence.Add(new Tuple<RawRecord, string>(record, record[adjacencyListSinkIndex].ToValue));
                }

                // When sinkVertexQuery is null, only sink vertices' IDs are to be returned. 
                // As a result, there is no need to send queries the underlying system to retrieve 
                // the sink vertices.  
                if (sinkVertexQuery == null)
                {
                    foreach (Tuple<RawRecord, string> pair in inputSequence)
                    {
                        RawRecord resultRecord = new RawRecord { fieldValues = new List<FieldObject>() };
                        resultRecord.Append(pair.Item1);
                        resultRecord.Append(new StringField(pair.Item2));
                        outputBuffer.Enqueue(resultRecord);
                    }

                    continue;
                }

                // Groups records returned by sinkVertexQuery by sink vertices' references
                Dictionary<string, List<RawRecord>> sinkVertexCollection = new Dictionary<string, List<RawRecord>>(GraphViewConnection.InClauseLimit);

                HashSet<string> sinkReferenceSet = new HashSet<string>();
                StringBuilder sinkReferenceList = new StringBuilder();
                // Given a list of sink references, sends queries to the underlying system
                // to retrieve the sink vertices. To reduce the number of queries to send,
                // we pack multiple sink references in one query using the IN clause, i.e., 
                // IN (ref1, ref2, ...). Since the total number of references to locate may exceed
                // the limit that is allowed in the IN clause, we may need to send more than one 
                // query to retrieve all sink vertices. 
                int j = 0;
                while (j < inputSequence.Count)
                {
                    sinkReferenceSet.Clear();

                    //TODO: Verify whether DocumentDB still has inClauseLimit
                    while (sinkReferenceSet.Count < GraphViewConnection.InClauseLimit && j < inputSequence.Count)
                    {
                        sinkReferenceSet.Add(inputSequence[j].Item2);
                        j++;
                    }

                    sinkReferenceList.Clear();
                    foreach (string sinkRef in sinkReferenceSet)
                    {
                        if (sinkReferenceList.Length > 0)
                        {
                            sinkReferenceList.Append(", ");
                        }
                        sinkReferenceList.AppendFormat("'{0}'", sinkRef);
                    }

                    string inClause = string.Format("{0}.id IN ({1})", sinkVertexQuery.Alias, sinkReferenceList.ToString());

                    JsonQuery toSendQuery = new JsonQuery()
                    {
                        Alias = sinkVertexQuery.Alias,
                        WhereSearchCondition = sinkVertexQuery.WhereSearchCondition,
                        SelectClause = sinkVertexQuery.SelectClause,
                        JoinClause = sinkVertexQuery.JoinClause,
                        ProjectedColumnsType = sinkVertexQuery.ProjectedColumnsType,
                        Properties = sinkVertexQuery.Properties,
                    };

                    if (toSendQuery.WhereSearchCondition == null)
                    {
                        toSendQuery.WhereSearchCondition = inClause;
                    }
                    else
                    {
                        toSendQuery.WhereSearchCondition = 
                            string.Format("({0}) AND {1}", sinkVertexQuery.WhereSearchCondition, inClause);
                    }

                    using (DbPortal databasePortal = connection.CreateDatabasePortal())
                    {
                        IEnumerator<RawRecord> verticesEnumerator = databasePortal.GetVertices(toSendQuery);

                        while (verticesEnumerator.MoveNext())
                        {
                            RawRecord rec = verticesEnumerator.Current;
                            if (!sinkVertexCollection.ContainsKey(rec[0].ToValue))
                            {
                                sinkVertexCollection.Add(rec[0].ToValue, new List<RawRecord>());
                            }
                            sinkVertexCollection[rec[0].ToValue].Add(rec);
                        }
                    }
                }

                foreach (Tuple<RawRecord, string> pair in inputSequence)
                {
                    if (!sinkVertexCollection.ContainsKey(pair.Item2))
                    {
                        continue;
                    }

                    RawRecord sourceRec = pair.Item1;
                    List<RawRecord> sinkRecList = sinkVertexCollection[pair.Item2];
                    
                    foreach (RawRecord sinkRec in sinkRecList)
                    {
                        if (matchingIndexes != null && matchingIndexes.Count > 0)
                        {
                            int k = 0;
                            for (; k < matchingIndexes.Count; k++)
                            {
                                int sourceMatchIndex = matchingIndexes[k].Item1;
                                int sinkMatchIndex = matchingIndexes[k].Item2;
                                if (!sourceRec[sourceMatchIndex].ToValue.Equals(sinkRec[sinkMatchIndex].ToValue, StringComparison.OrdinalIgnoreCase))
                                //if (sourceRec[sourceMatchIndex] != sinkRec[sinkMatchIndex])
                                {
                                    break;
                                }
                            }

                            // The source-sink record pair is the result only when it passes all matching tests. 
                            if (k < matchingIndexes.Count)
                            {
                                continue;
                            }
                        }

                        RawRecord resultRec = new RawRecord(sourceRec);
                        resultRec.Append(sinkRec);

                        outputBuffer.Enqueue(resultRec);
                    }
                }
            }

            if (outputBuffer.Count == 0)
            {
                if (!inputOp.State())
                    Close();
                return null;
            }
            else if (outputBuffer.Count == 1)
            {
                Close();
                return outputBuffer.Dequeue();
            }
            else
            {
                return outputBuffer.Dequeue();
            }
        }

        public override void ResetState()
        {
            inputOp.ResetState();
            outputBuffer?.Clear();
            Open();
        }
    }

    internal class BothVOperator : GraphViewExecutionOperator
    {
        private int outputBufferSize;
        private int batchSize = 100;
        private int inClauseLimit = 200;
        private Queue<RawRecord> outputBuffer;
        private GraphViewConnection connection;
        private GraphViewExecutionOperator inputOp;


        private List<int> adjacencyListSinkIndexes;

        // The query that describes predicates on the sink vertices and its properties to return.
        // It is null if the sink vertex has no predicates and no properties other than sink vertex ID
        // are to be returned.  
        private JsonQuery sinkVertexQuery;

        public BothVOperator(
            GraphViewExecutionOperator inputOp,
            GraphViewConnection connection,
            List<int> sinkIndexes,
            JsonQuery sinkVertexQuery,
            int outputBufferSize = 1000)
        {
            Open();
            this.inputOp = inputOp;
            this.connection = connection;
            this.adjacencyListSinkIndexes = sinkIndexes;
            this.sinkVertexQuery = sinkVertexQuery;
            this.outputBufferSize = outputBufferSize;
        }

        public override RawRecord Next()
        {
            if (outputBuffer == null)
            {
                outputBuffer = new Queue<RawRecord>(outputBufferSize);
            }

            while (outputBuffer.Count < outputBufferSize && inputOp.State())
            {
                List<Tuple<RawRecord, string>> inputSequence = new List<Tuple<RawRecord, string>>(batchSize);

                // Loads a batch of source records
                for (int i = 0; i < batchSize && inputOp.State(); i++)
                {
                    RawRecord record = inputOp.Next();
                    if (record == null)
                    {
                        break;
                    }

                    foreach (var adjacencyListSinkIndex in adjacencyListSinkIndexes)
                    {
                        inputSequence.Add(new Tuple<RawRecord, string>(record, record[adjacencyListSinkIndex].ToValue));
                    }
                }

                // When sinkVertexQuery is null, only sink vertices' IDs are to be returned. 
                // As a result, there is no need to send queries the underlying system to retrieve 
                // the sink vertices.  
                if (sinkVertexQuery == null)
                {
                    foreach (Tuple<RawRecord, string> pair in inputSequence)
                    {
                        RawRecord resultRecord = new RawRecord { fieldValues = new List<FieldObject>() };
                        resultRecord.Append(pair.Item1);
                        resultRecord.Append(new StringField(pair.Item2));
                        outputBuffer.Enqueue(resultRecord);
                    }

                    continue;
                }

                // Groups records returned by sinkVertexQuery by sink vertices' references
                Dictionary<string, List<RawRecord>> sinkVertexCollection = new Dictionary<string, List<RawRecord>>(inClauseLimit);

                HashSet<string> sinkReferenceSet = new HashSet<string>();
                StringBuilder sinkReferenceList = new StringBuilder();
                // Given a list of sink references, sends queries to the underlying system
                // to retrieve the sink vertices. To reduce the number of queries to send,
                // we pack multiple sink references in one query using the IN clause, i.e., 
                // IN (ref1, ref2, ...). Since the total number of references to locate may exceed
                // the limit that is allowed in the IN clause, we may need to send more than one 
                // query to retrieve all sink vertices. 
                int j = 0;
                while (j < inputSequence.Count)
                {
                    sinkReferenceSet.Clear();

                    //TODO: Verify whether DocumentDB still has inClauseLimit
                    while (sinkReferenceSet.Count < inClauseLimit && j < inputSequence.Count)
                    {
                        sinkReferenceSet.Add(inputSequence[j].Item2);
                        j++;
                    }

                    sinkReferenceList.Clear();
                    foreach (string sinkRef in sinkReferenceSet)
                    {
                        if (sinkReferenceList.Length > 0)
                        {
                            sinkReferenceList.Append(", ");
                        }
                        sinkReferenceList.AppendFormat("'{0}'", sinkRef);
                    }

                    string inClause = string.Format("{0}.id IN ({1})", sinkVertexQuery.Alias, sinkReferenceList.ToString());

                    JsonQuery toSendQuery = new JsonQuery()
                    {
                        Alias = sinkVertexQuery.Alias,
                        WhereSearchCondition = sinkVertexQuery.WhereSearchCondition,
                        SelectClause = sinkVertexQuery.SelectClause,
                        ProjectedColumnsType = sinkVertexQuery.ProjectedColumnsType,
                        Properties = sinkVertexQuery.Properties,
                    };

                    if (toSendQuery.WhereSearchCondition == null)
                    {
                        toSendQuery.WhereSearchCondition = inClause;
                    }
                    else
                    {
                        toSendQuery.WhereSearchCondition =
                            string.Format("({0}) AND {1}", sinkVertexQuery.WhereSearchCondition, inClause);
                    }

                    using (DbPortal databasePortal = connection.CreateDatabasePortal())
                    {
                        IEnumerator<RawRecord> verticesEnumerator = databasePortal.GetVertices(toSendQuery);

                        while (verticesEnumerator.MoveNext())
                        {
                            RawRecord rec = verticesEnumerator.Current;
                            if (!sinkVertexCollection.ContainsKey(rec[0].ToValue))
                            {
                                sinkVertexCollection.Add(rec[0].ToValue, new List<RawRecord>());
                            }
                            sinkVertexCollection[rec[0].ToValue].Add(rec);
                        }
                    }
                }

                foreach (Tuple<RawRecord, string> pair in inputSequence)
                {
                    if (!sinkVertexCollection.ContainsKey(pair.Item2))
                    {
                        continue;
                    }

                    RawRecord sourceRec = pair.Item1;
                    List<RawRecord> sinkRecList = sinkVertexCollection[pair.Item2];

                    foreach (RawRecord sinkRec in sinkRecList)
                    {
                        RawRecord resultRec = new RawRecord(sourceRec);
                        resultRec.Append(sinkRec);

                        outputBuffer.Enqueue(resultRec);
                    }
                }
            }

            if (outputBuffer.Count == 0)
            {
                if (!inputOp.State())
                    Close();
                return null;
            }
            else if (outputBuffer.Count == 1)
            {
                Close();
                return outputBuffer.Dequeue();
            }
            else
            {
                return outputBuffer.Dequeue();
            }
        }

        public override void ResetState()
        {
            inputOp.ResetState();
            outputBuffer?.Clear();
            Open();
        }
    }

    internal class FilterOperator : GraphViewExecutionOperator
    {
        public GraphViewExecutionOperator Input { get; private set; }
        public BooleanFunction Func { get; private set; }

        public FilterOperator(GraphViewExecutionOperator input, BooleanFunction func)
        {
            Input = input;
            Func = func;
            Open();
        }

        public override RawRecord Next()
        {
            RawRecord rec;
            while (Input.State() && (rec = Input.Next()) != null)
            {
                if (Func.Evaluate(rec))
                {
                    return rec;
                }
            }

            Close();
            return null;
        }

        public override void ResetState()
        {
            Input.ResetState();
            Open();
        }
    }

    internal class CartesianProductOperator2 : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator leftInput;
        private ContainerEnumerator rightInputEnumerator;
        private RawRecord leftRecord;

        public CartesianProductOperator2(
            GraphViewExecutionOperator leftInput, 
            GraphViewExecutionOperator rightInput)
        {
            this.leftInput = leftInput;
            ContainerOperator rightInputContainer = new ContainerOperator(rightInput);
            rightInputEnumerator = rightInputContainer.GetEnumerator();
            leftRecord = null;
            Open();
        }

        public override RawRecord Next()
        {
            RawRecord cartesianRecord = null;

            while (cartesianRecord == null && State())
            {
                if (leftRecord == null && leftInput.State())
                {
                    leftRecord = leftInput.Next();
                }

                if (leftRecord == null)
                {
                    Close();
                    break;
                }
                else
                {
                    if (rightInputEnumerator.MoveNext())
                    {
                        RawRecord rightRecord = rightInputEnumerator.Current;
                        cartesianRecord = new RawRecord(leftRecord);
                        cartesianRecord.Append(rightRecord);
                    }
                    else
                    {
                        // For the current left record, the enumerator on the right input has reached the end.
                        // Moves to the next left record and resets the enumerator.
                        rightInputEnumerator.Reset();
                        leftRecord = null;
                    }
                }
            }

            return cartesianRecord;
        }

        public override void ResetState()
        {
            leftInput.ResetState();
            rightInputEnumerator.ResetState();
            Open();
        }
    }

    //internal class AdjacencyListDecoder : TableValuedFunction
    //{
    //    protected List<int> AdjacencyListIndexes;
    //    protected BooleanFunction EdgePredicate;
    //    protected List<string> ProjectedFields;

    //    public AdjacencyListDecoder(GraphViewExecutionOperator input, List<int> adjacencyListIndexes,
    //        BooleanFunction edgePredicate, List<string> projectedFields, int outputBufferSize = 1000)
    //        : base(input, outputBufferSize)
    //    {
    //        this.AdjacencyListIndexes = adjacencyListIndexes;
    //        this.EdgePredicate = edgePredicate;
    //        this.ProjectedFields = projectedFields;
    //    }

    //    internal override IEnumerable<RawRecord> CrossApply(RawRecord record)
    //    {
    //        List<RawRecord> results = new List<RawRecord>();

    //        foreach (var adjIndex in AdjacencyListIndexes)
    //        {
    //            string jsonArray = record[adjIndex].ToString();
    //            // Parse the adj list in JSON array
    //            var adj = JArray.Parse(jsonArray);
    //            foreach (var edge in adj.Children<JObject>())
    //            {
    //                // Construct new record
    //                var result = new RawRecord(ProjectedFields.Count);

    //                // Fill the field of selected edge's properties
    //                for (var i = 0; i < ProjectedFields.Count; i++)
    //                {
    //                    var projectedField = ProjectedFields[i];
    //                    var fieldValue = "*".Equals(projectedField, StringComparison.OrdinalIgnoreCase)
    //                        ? edge
    //                        : edge[projectedField];

    //                    result.fieldValues[i] = fieldValue != null ? new StringField(fieldValue.ToString()) : null;
    //                }

    //                results.Add(result);
    //            }
    //        }

    //        return results;
    //    }

    //    public override RawRecord Next()
    //    {
    //        if (outputBuffer == null)
    //            outputBuffer = new Queue<RawRecord>();

    //        while (outputBuffer.Count < outputBufferSize && inputOperator.State())
    //        {
    //            RawRecord srcRecord = inputOperator.Next();
    //            if (srcRecord == null)
    //                break;

    //            var results = CrossApply(srcRecord);
    //            foreach (var edgeRecord in results)
    //            {
    //                if (edgePredicate != null && !edgePredicate.Evaluate(edgeRecord))
    //                    continue;

    //                var resultRecord = new RawRecord(srcRecord);
    //                resultRecord.Append(edgeRecord);
    //                outputBuffer.Enqueue(resultRecord);
    //            }
    //        }

    //        if (outputBuffer.Count == 0)
    //        {
    //            if (!inputOperator.State())
    //                Close();
    //            return null;
    //        }
    //        else if (outputBuffer.Count == 1)
    //        {
    //            Close();
    //            return outputBuffer.Dequeue();
    //        }
    //        else
    //        {
    //            return outputBuffer.Dequeue();
    //        }
    //    }

    //    public override void ResetState()
    //    {
    //        inputOperator.ResetState();
    //        outputBuffer?.Clear();
    //        Open();
    //    }
    //}

    internal class AdjacencyListDecoder2 : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator inputOperator;
        private int startVertexIndex;
        private int startVertexLabelIndex;

        private int adjacencyListIndex;
        private int revAdjacencyListIndex;

        private BooleanFunction edgePredicate;
        private List<string> projectedFields;

        private bool isStartVertexTheOriginVertex;

        private Queue<RawRecord> outputBuffer;
        private GraphViewConnection connection;

        private int batchSize;
        private bool isBatchingReverseEdgeMode;
        private Queue<Tuple<RawRecord, string>> inputSequence;
        private Dictionary<string, AdjacencyListField> reverseAdjacencyListCollection;

        public AdjacencyListDecoder2(GraphViewExecutionOperator input,
            int startVertexIndex, int startVertexLabelIndex, int adjacencyListIndex, int revAdjacencyListIndex, 
            bool isStartVertexTheOriginVertex,
            BooleanFunction edgePredicate, List<string> projectedFields,
            GraphViewConnection connection,
            int batchSize = 1000)
        {
            this.inputOperator = input;
            this.outputBuffer = new Queue<RawRecord>();
            this.startVertexIndex = startVertexIndex;
            this.startVertexLabelIndex = startVertexLabelIndex;
            this.adjacencyListIndex = adjacencyListIndex;
            this.revAdjacencyListIndex = revAdjacencyListIndex;
            this.isStartVertexTheOriginVertex = isStartVertexTheOriginVertex;
            this.edgePredicate = edgePredicate;
            this.projectedFields = projectedFields;
            this.connection = connection;

            this.batchSize = batchSize;
            this.isBatchingReverseEdgeMode = this.revAdjacencyListIndex >= 0 && !this.connection.UseReverseEdges;
            this.inputSequence = new Queue<Tuple<RawRecord, string>>();
            this.reverseAdjacencyListCollection = new Dictionary<string, AdjacencyListField>();

            Open();
        }

        /// <summary>
        /// Fill edge's {_source, _sink, _other, _offset, *} meta fields
        /// </summary>
        /// <param name="record"></param>
        /// <param name="edge"></param>
        /// <param name="startVertexId"></param>
        /// <param name="isReversedAdjList"></param>
        private void FillMetaField(RawRecord record, EdgeField edge, string startVertexId, string startVertexLabel, bool isReversedAdjList)
        {
            string otherValue;
            if (this.isStartVertexTheOriginVertex) {
                if (isReversedAdjList) {
                    otherValue = edge["_srcV"].ToValue;
                }
                else {
                    otherValue = edge["_sinkV"].ToValue;
                }
            }
            else {
                otherValue = startVertexId;
            }

            record.fieldValues[0] = new StringField(edge.OutV);
            record.fieldValues[1] = new StringField(edge.InV);
            record.fieldValues[2] = new StringField(otherValue);
            record.fieldValues[3] = new StringField(edge.Offset.ToString());
            record.fieldValues[4] = edge;
        }

        /// <summary>
        /// Fill the field of selected edge's properties
        /// </summary>
        /// <param name="record"></param>
        /// <param name="edge"></param>
        private void FillPropertyField(RawRecord record, EdgeField edge)
        {
            for (var i = GraphViewReservedProperties.ReservedEdgeProperties.Count; i < projectedFields.Count; i++)
            {
                record.fieldValues[i] = edge[projectedFields[i]];
            }
        }

        /// <summary>
        /// Decode an adjacency list and return all the edges satisfying the edge predicate
        /// </summary>
        /// <param name="adjacencyList"></param>
        /// <param name="startVertexId"></param>
        /// <param name="startVertexLabel"></param>
        /// <param name="isReverse"></param>
        /// <returns></returns>
        private List<RawRecord> DecodeAdjacencyList(AdjacencyListField adjacencyList, string startVertexId, string startVertexLabel, bool isReverse)
        {
            List<RawRecord> edgeRecordCollection = new List<RawRecord>();

            foreach (EdgeField edge in adjacencyList.AllEdges)
            {
                // Construct new record
                RawRecord edgeRecord = new RawRecord(projectedFields.Count);

                FillMetaField(edgeRecord, edge, startVertexId, startVertexLabel, isReverse);
                FillPropertyField(edgeRecord, edge);

                if (edgePredicate != null && !edgePredicate.Evaluate(edgeRecord))
                    continue;

                edgeRecordCollection.Add(edgeRecord);
            }

            return edgeRecordCollection;
        }

        /// <summary>
        /// Decode a record's adjacency list or/and reverse adjacency list
        /// and return all the edges satisfying the edge predicate
        /// </summary>
        /// <param name="record"></param>
        /// <returns></returns>
        private List<RawRecord> Decode(RawRecord record)
        {
            List<RawRecord> results = new List<RawRecord>();
            string startVertexId = record[startVertexIndex].ToValue;
            string startVertexLabel = record[startVertexLabelIndex]?.ToValue;

            if (adjacencyListIndex >= 0)
            {
                AdjacencyListField adj = record[adjacencyListIndex] as AdjacencyListField;
                if (adj == null)
                    throw new GraphViewException(string.Format("The FieldObject at {0} is not a adjacency list but {1}", 
                        adjacencyListIndex, record[adjacencyListIndex] != null ? record[adjacencyListIndex].ToString() : "null"));

                results.AddRange(DecodeAdjacencyList(adj, startVertexId, startVertexLabel, false));
            }

            if (revAdjacencyListIndex >= 0 && connection.UseReverseEdges)
            {
                AdjacencyListField adj = record[revAdjacencyListIndex] as AdjacencyListField;

                if (adj == null)
                    throw new GraphViewException(string.Format("The FieldObject at {0} is not a reverse adjacency list but {1}",
                        revAdjacencyListIndex, record[revAdjacencyListIndex] != null ? record[revAdjacencyListIndex].ToString() : "null"));

                results.AddRange(DecodeAdjacencyList(adj, startVertexId, startVertexLabel, true));
            }

            return results;
        }

        /// <summary>
        /// Cross apply the adjacency list or/and reverse adjacency list of the record
        /// </summary>
        /// <param name="record"></param>
        /// <returns></returns>
        private List<RawRecord> CrossApply(RawRecord record)
        {
            List<RawRecord> results = new List<RawRecord>();

            foreach (RawRecord edgeRecord in Decode(record))
            {
                RawRecord r = new RawRecord(record);
                r.Append(edgeRecord);

                results.Add(r);
            }

            return results;
        }

        /// <summary>
        /// Send one query to get all the reverse adjacency lists of vertice in the inputSequence 
        /// </summary>
        private void GetReverseAdjacencyListsInBatch()
        {
            HashSet<string> vertexIdCollection = new HashSet<string>();
            foreach (Tuple<RawRecord, string> tuple in inputSequence) {
                vertexIdCollection.Add(tuple.Item2);
            }

            reverseAdjacencyListCollection = EdgeDocumentHelper.GetReverseAdjacencyListsOfVertexCollection(connection, vertexIdCollection);
        }

        /// <summary>
        /// Cross apply the reverse adjacency list of one record in the inputSequence
        /// </summary>
        /// <returns></returns>
        private List<RawRecord> CrossApplyOneRecordinInputSequence()
        {
            List<RawRecord> results = new List<RawRecord>();
            Tuple<RawRecord, string> inputTuple = inputSequence.Dequeue();

            RawRecord record = inputTuple.Item1;
            string vertexId = inputTuple.Item2;
            string startVertexLabel = record[startVertexLabelIndex]?.ToValue;

            AdjacencyListField adj = reverseAdjacencyListCollection[vertexId];

            foreach (RawRecord edgeRecord in DecodeAdjacencyList(adj, vertexId, startVertexLabel, true))
            {
                RawRecord r = new RawRecord(record);
                r.Append(edgeRecord);

                results.Add(r);
            }

            reverseAdjacencyListCollection.Remove(vertexId);

            return results;
        }

        public override RawRecord Next()
        {
            if (outputBuffer.Count > 0) {
                return outputBuffer.Dequeue();
            }

            while (inputSequence.Count >= batchSize 
                || reverseAdjacencyListCollection.Count > 0 
                || (inputSequence.Count != 0 && !inputOperator.State()))
            {
                if (reverseAdjacencyListCollection.Count == 0) {
                    GetReverseAdjacencyListsInBatch();
                }

                foreach (RawRecord record in CrossApplyOneRecordinInputSequence()) {
                    outputBuffer.Enqueue(record);
                }

                if (outputBuffer.Count > 0) {
                    return outputBuffer.Dequeue();
                }
            }

            while (inputOperator.State())
            {
                RawRecord currentRecord = inputOperator.Next();

                if (currentRecord == null) {
                    break;
                }

                if (isBatchingReverseEdgeMode && inputSequence.Count < batchSize) {
                    inputSequence.Enqueue(new Tuple<RawRecord, string>(currentRecord, currentRecord[startVertexIndex].ToValue));
                }

                foreach (RawRecord record in CrossApply(currentRecord)) {
                    outputBuffer.Enqueue(record);
                }

                if (outputBuffer.Count > 0) {
                    return outputBuffer.Dequeue();
                }
            }

            while (inputSequence.Count >= batchSize
                || reverseAdjacencyListCollection.Count > 0
                || (inputSequence.Count != 0 && !inputOperator.State()))
            {
                if (reverseAdjacencyListCollection.Count == 0) {
                    GetReverseAdjacencyListsInBatch();
                }

                foreach (RawRecord record in CrossApplyOneRecordinInputSequence()) {
                    outputBuffer.Enqueue(record);
                }

                if (outputBuffer.Count > 0) {
                    return outputBuffer.Dequeue();
                }
            }

            Close();
            return null;
        }

        public override void ResetState()
        {
            inputOperator.ResetState();
            outputBuffer?.Clear();
            inputSequence?.Clear();
            reverseAdjacencyListCollection?.Clear();
            Open();
        }
    }

    //internal abstract class TableValuedScalarFunction
    //{
    //    public abstract IEnumerable<string> Apply(RawRecord record);
    //}

    //internal class CrossApplyAdjacencyList : TableValuedScalarFunction
    //{
    //    private int adjacencyListIndex;

    //    public CrossApplyAdjacencyList(int adjacencyListIndex)
    //    {
    //        this.adjacencyListIndex = adjacencyListIndex;
    //    }

    //    public override IEnumerable<string> Apply(RawRecord record)
    //    {
    //        throw new NotImplementedException();
    //    }
    //}

    //internal class CrossApplyPath : TableValuedScalarFunction
    //{
    //    private GraphViewExecutionOperator referenceOp;
    //    private ConstantSourceOperator contextScan;
    //    private ExistsFunction terminateFunction;
    //    private int iterationUpperBound;

    //    public CrossApplyPath(
    //        ConstantSourceOperator contextScan, 
    //        GraphViewExecutionOperator referenceOp,
    //        int iterationUpperBound)
    //    {
    //        this.contextScan = contextScan;
    //        this.referenceOp = referenceOp;
    //        this.iterationUpperBound = iterationUpperBound;
    //    }

    //    public CrossApplyPath(
    //        ConstantSourceOperator contextScan,
    //        GraphViewExecutionOperator referenceOp,
    //        ExistsFunction terminateFunction)
    //    {
    //        this.contextScan = contextScan;
    //        this.referenceOp = referenceOp;
    //        this.terminateFunction = terminateFunction;
    //    }

    //    public override IEnumerable<string> Apply(RawRecord record)
    //    {
    //        contextScan.ConstantSource = record;

    //        if (terminateFunction != null)
    //        {
    //            throw new NotImplementedException();
    //        }
    //        else
    //        {
    //            throw new NotImplementedException();
    //        }
    //    }
    //}

    internal class OrderOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator inputOp;
        private List<RawRecord> inputBuffer;
        private int returnIndex;

        private List<Tuple<ScalarFunction, IComparer>> orderByElements;

        public OrderOperator(GraphViewExecutionOperator inputOp, List<Tuple<ScalarFunction, IComparer>> orderByElements)
        {
            this.Open();
            this.inputOp = inputOp;
            this.orderByElements = orderByElements;
            this.returnIndex = 0;
        }

        public override RawRecord Next()
        {
            if (this.inputBuffer == null)
            {
                this.inputBuffer = new List<RawRecord>();

                RawRecord inputRec = null;
                while (this.inputOp.State() && (inputRec = this.inputOp.Next()) != null) {
                    this.inputBuffer.Add(inputRec);
                }

                this.inputBuffer.Sort((x, y) =>
                {
                    int ret = 0;
                    foreach (Tuple<ScalarFunction, IComparer> orderByElement in this.orderByElements)
                    {
                        ScalarFunction byFunction = orderByElement.Item1;

                        FieldObject xKey = byFunction.Evaluate(x);
                        if (xKey == null) {
                            throw new GraphViewException("The provided traversal or property name of Order does not map to a value.");
                        }

                        FieldObject yKey = byFunction.Evaluate(y);
                        if (yKey == null) {
                            throw new GraphViewException("The provided traversal or property name of Order does not map to a value.");
                        }

                        IComparer comparer = orderByElement.Item2;
                        ret = comparer.Compare(xKey.ToObject(), yKey.ToObject());

                        if (ret != 0) break;
                    }
                    return ret;
                });
            }

            while (this.returnIndex < this.inputBuffer.Count) {
                return this.inputBuffer[this.returnIndex++];
            }

            this.Close();
            return null;
        }

        public override void ResetState()
        {
            this.inputBuffer = null;
            this.inputOp.ResetState();
            this.returnIndex = 0;

            this.Open();
        }
    }

    internal class OrderLocalOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator inputOp;
        private int inputObjectIndex;
        private List<Tuple<ScalarFunction, IComparer>> orderByElements;
        private ByColumn byColumn;
        private IComparer byColumnComparer;

        enum ByColumn
        {
            NONE, KEYS, VALUES
        }

        public OrderLocalOperator(GraphViewExecutionOperator inputOp, int inputObjectIndex, List<Tuple<ScalarFunction, IComparer>> orderByElements)
        {
            this.inputOp = inputOp;
            this.inputObjectIndex = inputObjectIndex;
            this.orderByElements = orderByElements;
            this.Open();
        }

        public override RawRecord Next()
        {
            RawRecord srcRecord = null;

            while (this.inputOp.State() && (srcRecord = this.inputOp.Next()) != null)
            {
                FieldObject inputObject = srcRecord[this.inputObjectIndex];
                if (inputObject is CollectionField)
                {
                    CollectionField inputCollection = (CollectionField)inputObject;
                    inputCollection.Collection.Sort((x, y) =>
                    {
                        int ret = 0;
                        foreach (Tuple<ScalarFunction, IComparer> tuple in this.orderByElements)
                        {
                            ScalarFunction byFunction = tuple.Item1;

                            RawRecord initCompose1RecordOfX = new RawRecord();
                            initCompose1RecordOfX.Append(x);
                            FieldObject xKey = byFunction.Evaluate(initCompose1RecordOfX);
                            if (xKey == null) {
                                throw new GraphViewException("The provided traversal or property name of Order(local) does not map to a value.");
                            }

                            RawRecord initCompose1RecordOfY = new RawRecord();
                            initCompose1RecordOfX.Append(y);
                            FieldObject yKey = byFunction.Evaluate(initCompose1RecordOfY);
                            if (yKey == null) {
                                throw new GraphViewException("The provided traversal or property name of Order(local) does not map to a value.");
                            }

                            IComparer comparer = tuple.Item2;
                            ret = comparer.Compare(xKey.ToObject(), yKey.ToObject());

                            if (ret != 0) break;
                        }
                        return ret;
                    });
                }
                else if (inputObject is MapField)
                {
                    MapField inputMap = (MapField) inputObject;
                    if (this.byColumn == ByColumn.KEYS) {
                        inputMap.Order.Sort((x, y) => this.byColumnComparer.Compare(x.ToObject(), y.ToObject()));
                    }
                    else if (this.byColumn == ByColumn.VALUES)
                    {
                        inputMap.Order.Sort(
                            (x, y) => this.byColumnComparer.Compare(inputMap[x].ToObject(), inputMap[y].ToObject()));
                    }
                    else
                    {
                        //TODO: Sync with Jinjin
                    }
                }

                return srcRecord;
            }

            this.Close();
            return null;
        }

        public override void ResetState()
        {
            this.inputOp.ResetState();
            this.Open();
        }
    }

    internal interface IAggregateFunction
    {
        void Init();
        void Accumulate(params FieldObject[] values);
        FieldObject Terminate();
    }

    internal class ProjectOperator : GraphViewExecutionOperator
    {
        private List<ScalarFunction> selectScalarList;
        private GraphViewExecutionOperator inputOp;

        private RawRecord currentRecord;

        public ProjectOperator(GraphViewExecutionOperator inputOp)
        {
            this.Open();
            this.inputOp = inputOp;
            selectScalarList = new List<ScalarFunction>();
        }

        public void AddSelectScalarElement(ScalarFunction scalarFunction)
        {
            selectScalarList.Add(scalarFunction);
        }

        public override RawRecord Next()
        {
            currentRecord = inputOp.State() ? inputOp.Next() : null;
            if (currentRecord == null)
            {
                Close();
                return null;
            }

            RawRecord selectRecord = new RawRecord(selectScalarList.Count);
            int index = 0;
            foreach (var scalarFunction in selectScalarList)
            {
                // TODO: Skip * for now, need refactor
                // if (scalarFunction == null) continue;
                if (scalarFunction != null)
                {
                    FieldObject result = scalarFunction.Evaluate(currentRecord);
                    selectRecord.fieldValues[index++] = result;
                }
                else
                {
                    selectRecord.fieldValues[index++] = null;
                }
            }

            return selectRecord;
        }

        public override void ResetState()
        {
            currentRecord = null;
            inputOp.ResetState();
            Open();
        }
    }

    internal class ProjectAggregation : GraphViewExecutionOperator
    {
        List<Tuple<IAggregateFunction, List<ScalarFunction>>> aggregationSpecs;
        GraphViewExecutionOperator inputOp;

        public ProjectAggregation(GraphViewExecutionOperator inputOp)
        {
            this.inputOp = inputOp;
            aggregationSpecs = new List<Tuple<IAggregateFunction, List<ScalarFunction>>>();
            Open();
        }

        public void AddAggregateSpec(IAggregateFunction aggrFunc, List<ScalarFunction> aggrInput)
        {
            aggregationSpecs.Add(new Tuple<IAggregateFunction, List<ScalarFunction>>(aggrFunc, aggrInput));
        }

        public override void ResetState()
        {
            inputOp.ResetState();
            foreach (var aggr in aggregationSpecs)
            {
                if (aggr.Item1 != null)
                {
                    aggr.Item1.Init();
                }
            }
            Open();
        }

        public override RawRecord Next()
        {
            if (!State())
                return null;

            foreach (var aggr in aggregationSpecs)
            {
                if (aggr.Item1 != null)
                {
                    aggr.Item1.Init();
                }
            }

            RawRecord inputRec = null;
            while (inputOp.State() && (inputRec = inputOp.Next()) != null)
            {
                foreach (var aggr in aggregationSpecs)
                {
                    IAggregateFunction aggregate = aggr.Item1;
                    List<ScalarFunction> parameterFunctions = aggr.Item2;

                    if (aggregate == null)
                    {
                        continue;
                    }

                    FieldObject[] paraList = new FieldObject[aggr.Item2.Count];
                    for(int i = 0; i < parameterFunctions.Count; i++)
                    {
                        paraList[i] = parameterFunctions[i].Evaluate(inputRec); 
                    }

                    aggregate.Accumulate(paraList);
                }
            }

            RawRecord outputRec = new RawRecord();
            foreach (var aggr in aggregationSpecs)
            {
                if (aggr.Item1 != null)
                {
                    outputRec.Append(aggr.Item1.Terminate());
                }
                else
                {
                    outputRec.Append((StringField)null);
                }
            }

            Close();
            return outputRec;
        }
    }

    internal class MapOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator inputOp;

        // The traversal inside the map function.
        private GraphViewExecutionOperator mapTraversal;
        private ConstantSourceOperator contextOp;

        public MapOperator(
            GraphViewExecutionOperator inputOp,
            GraphViewExecutionOperator mapTraversal,
            ConstantSourceOperator contextOp)
        {
            this.inputOp = inputOp;
            this.mapTraversal = mapTraversal;
            this.contextOp = contextOp;
            Open();
        }

        public override RawRecord Next()
        {
            RawRecord currentRecord;
            while (inputOp.State() && (currentRecord = inputOp.Next()) != null)
            {
                contextOp.ConstantSource = currentRecord;
                mapTraversal.ResetState();
                RawRecord mapRec = mapTraversal.Next();
                mapTraversal.Close();

                if (mapRec == null) continue;
                RawRecord resultRecord = new RawRecord(currentRecord);
                resultRecord.Append(mapRec);

                return resultRecord;
            }

            Close();
            return null;
        }

        public override void ResetState()
        {
            inputOp.ResetState();
            contextOp.ResetState();
            mapTraversal.ResetState();
            Open();
        }
    }

    internal class FlatMapOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator inputOp;

        // The traversal inside the flatMap function.
        private GraphViewExecutionOperator flatMapTraversal;
        private ConstantSourceOperator contextOp;

        private RawRecord currentRecord = null;
        private Queue<RawRecord> outputBuffer;

        public FlatMapOperator(
            GraphViewExecutionOperator inputOp,
            GraphViewExecutionOperator flatMapTraversal,
            ConstantSourceOperator contextOp)
        {
            this.inputOp = inputOp;
            this.flatMapTraversal = flatMapTraversal;
            this.contextOp = contextOp;
            
            outputBuffer = new Queue<RawRecord>();
            Open();
        }

        public override RawRecord Next()
        {
            if (outputBuffer.Count > 0)
            {
                RawRecord r = new RawRecord(currentRecord);
                RawRecord toAppend = outputBuffer.Dequeue();
                r.Append(toAppend);

                return r;
            }

            while (inputOp.State())
            {
                currentRecord = inputOp.Next();
                if (currentRecord == null)
                {
                    Close();
                    return null;
                }

                contextOp.ConstantSource = currentRecord;
                flatMapTraversal.ResetState();
                RawRecord flatMapRec = null;
                while ((flatMapRec = flatMapTraversal.Next()) != null)
                {
                    outputBuffer.Enqueue(flatMapRec);
                }

                if (outputBuffer.Count > 0)
                {
                    RawRecord r = new RawRecord(currentRecord);
                    RawRecord toAppend = outputBuffer.Dequeue();
                    r.Append(toAppend);

                    return r;
                }
            }

            Close();
            return null;
        }

        public override void ResetState()
        {
            currentRecord = null;
            inputOp.ResetState();
            contextOp.ResetState();
            flatMapTraversal.ResetState();
            outputBuffer?.Clear();
            Open();
        }
    }

    internal class LocalOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator inputOp;

        // The traversal inside the local function.
        private GraphViewExecutionOperator localTraversal;
        private ConstantSourceOperator contextOp;

        private RawRecord currentRecord = null;
        private Queue<RawRecord> outputBuffer;

        public LocalOperator(
            GraphViewExecutionOperator inputOp,
            GraphViewExecutionOperator localTraversal,
            ConstantSourceOperator contextOp)
        {
            this.inputOp = inputOp;
            this.localTraversal = localTraversal;
            this.contextOp = contextOp;

            outputBuffer = new Queue<RawRecord>();
            Open();
        }

        public override RawRecord Next()
        {
            if (outputBuffer.Count > 0)
            {
                RawRecord r = new RawRecord(currentRecord);
                RawRecord toAppend = outputBuffer.Dequeue();
                r.Append(toAppend);

                return r;
            }

            while (inputOp.State())
            {
                currentRecord = inputOp.Next();
                if (currentRecord == null)
                {
                    Close();
                    return null;
                }

                contextOp.ConstantSource = currentRecord;
                localTraversal.ResetState();
                RawRecord localRec = null;
                while ((localRec = localTraversal.Next()) != null)
                {
                    outputBuffer.Enqueue(localRec);
                }

                if (outputBuffer.Count > 0)
                {
                    RawRecord r = new RawRecord(currentRecord);
                    RawRecord toAppend = outputBuffer.Dequeue();
                    r.Append(toAppend);

                    return r;
                }
            }

            Close();
            return null;
        }

        public override void ResetState()
        {
            currentRecord = null;
            inputOp.ResetState();
            contextOp.ResetState();
            localTraversal.ResetState();
            outputBuffer?.Clear();
            Open();
        }
    }

    internal class OptionalOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator inputOp;
        // A list of record fields (identified by field indexes) from the input 
        // operator are to be returned when the optional traversal produces no results.
        // When a field index is less than 0, it means that this field value is always null. 
        private List<int> inputIndexes;

        // The traversal inside the optional function. 
        // The records returned by this operator should have the same number of fields
        // as the records drawn from the input operator, i.e., inputIndexes.Count 
        private GraphViewExecutionOperator optionalTraversal;
        private ConstantSourceOperator contextOp;
        private ContainerOperator rootContainerOp;

        private RawRecord currentRecord = null;
        private Queue<RawRecord> outputBuffer;

        private bool isCarryOnMode;
        private bool optionalTraversalHasResults;
        private bool hasReset;

        public OptionalOperator(
            GraphViewExecutionOperator inputOp,
            List<int> inputIndexes,
            GraphViewExecutionOperator optionalTraversal,
            ConstantSourceOperator contextOp,
            ContainerOperator containerOp,
            bool isCarryOnMode)
        {
            this.inputOp = inputOp;
            this.inputIndexes = inputIndexes;
            this.optionalTraversal = optionalTraversal;
            this.contextOp = contextOp;
            this.rootContainerOp = containerOp;

            this.isCarryOnMode = isCarryOnMode;
            this.optionalTraversalHasResults = false;
            this.hasReset = false;

            outputBuffer = new Queue<RawRecord>();
            Open();
        }

        public override RawRecord Next()
        {
            if (isCarryOnMode)
            {
                RawRecord traversalRecord;
                while (optionalTraversal.State() && (traversalRecord = optionalTraversal.Next()) != null)
                {
                    optionalTraversalHasResults = true;
                    return traversalRecord;
                }

                if (optionalTraversalHasResults)
                {
                    Close();
                    return null;
                }
                else
                {
                    if (!hasReset)
                    {
                        hasReset = true;
                        contextOp.ResetState();
                    }
                        
                    RawRecord inputRecord = null;
                    while (contextOp.State() && (inputRecord = contextOp.Next()) != null)
                    {
                        RawRecord r = new RawRecord(inputRecord);
                        foreach (int index in inputIndexes)
                        {
                            if (index < 0)
                            {
                                r.Append((FieldObject)null);
                            }
                            else
                            {
                                r.Append(inputRecord[index]);
                            }
                        }

                        return r;
                    }

                    Close();
                    return null;
                }
            }
            else
            {
                if (outputBuffer.Count > 0)
                {
                    RawRecord r = new RawRecord(currentRecord);
                    RawRecord toAppend = outputBuffer.Dequeue();
                    r.Append(toAppend);

                    return r;
                }

                while (inputOp.State())
                {
                    currentRecord = inputOp.Next();
                    if (currentRecord == null)
                    {
                        Close();
                        return null;
                    }

                    contextOp.ConstantSource = currentRecord;
                    optionalTraversal.ResetState();
                    RawRecord optionalRec = null;
                    while ((optionalRec = optionalTraversal.Next()) != null)
                    {
                        outputBuffer.Enqueue(optionalRec);
                    }

                    if (outputBuffer.Count > 0)
                    {
                        RawRecord r = new RawRecord(currentRecord);
                        RawRecord toAppend = outputBuffer.Dequeue();
                        r.Append(toAppend);

                        return r;
                    }
                    else
                    {
                        RawRecord r = new RawRecord(currentRecord);
                        foreach (int index in inputIndexes)
                        {
                            if (index < 0)
                            {
                                r.Append((FieldObject)null);
                            }
                            else
                            {
                                r.Append(currentRecord[index]);
                            }
                        }

                        return r;
                    }
                }

                Close();
                return null;
            }
        }

        public override void ResetState()
        {
            currentRecord = null;
            inputOp.ResetState();
            contextOp.ResetState();
            rootContainerOp?.ResetState();
            optionalTraversal.ResetState();
            outputBuffer?.Clear();
            Open();
        }
    }

    internal class UnionOperator : GraphViewExecutionOperator
    {
        private List<Tuple<ConstantSourceOperator, GraphViewExecutionOperator>> traversalList;
        private int activeTraversalIndex;
        private ContainerOperator rootContainerOp;
        //
        // Only for union() without any branch
        //
        private GraphViewExecutionOperator inputOp;

        public UnionOperator(GraphViewExecutionOperator inputOp, ContainerOperator containerOp)
        {
            this.inputOp = inputOp;
            this.rootContainerOp = containerOp;
            traversalList = new List<Tuple<ConstantSourceOperator, GraphViewExecutionOperator>>();
            Open();
            activeTraversalIndex = 0;
        }

        public void AddTraversal(ConstantSourceOperator contextOp, GraphViewExecutionOperator traversal)
        {
            traversalList.Add(new Tuple<ConstantSourceOperator, GraphViewExecutionOperator>(contextOp, traversal));
        }

        public override RawRecord Next()
        {
            //
            // Even the union() has no branch, the input still needs to be drained for cases like g.V().addV().union()
            //
            if (traversalList.Count == 0)
            {
                while (inputOp.State())
                {
                    inputOp.Next();
                }

                Close();
                return null;
            }

            RawRecord traversalRecord = null;
            while (traversalRecord == null && activeTraversalIndex < traversalList.Count)
            {
                GraphViewExecutionOperator activeOp = traversalList[activeTraversalIndex].Item2;
                if (activeOp.State() && (traversalRecord = activeOp.Next()) != null)
                {
                    break;
                }
                else
                {
                    activeTraversalIndex++;
                }
            }

            if (traversalRecord == null)
            {
                Close();
                return null;
            }
            else
            {
                return traversalRecord;
            }
        }

        public override void ResetState()
        {
            if (traversalList.Count == 0) {
                inputOp.ResetState();
            }

            foreach (Tuple<ConstantSourceOperator, GraphViewExecutionOperator> tuple in traversalList) {
                tuple.Item2.ResetState();
            }

            rootContainerOp.ResetState();
            activeTraversalIndex = 0;
            Open();
        }
    }

    internal class CoalesceOperator2 : GraphViewExecutionOperator
    {
        private List<Tuple<ConstantSourceOperator, GraphViewExecutionOperator>> traversalList;
        private GraphViewExecutionOperator inputOp;

        private RawRecord currentRecord;
        private Queue<RawRecord> traversalOutputBuffer;

        public CoalesceOperator2(GraphViewExecutionOperator inputOp)
        {
            this.inputOp = inputOp;
            traversalList = new List<Tuple<ConstantSourceOperator, GraphViewExecutionOperator>>();
            traversalOutputBuffer = new Queue<RawRecord>();
            Open();
        }

        public void AddTraversal(ConstantSourceOperator contextOp, GraphViewExecutionOperator traversal)
        {
            traversalList.Add(new Tuple<ConstantSourceOperator, GraphViewExecutionOperator>(contextOp, traversal));
        }

        public override RawRecord Next()
        {
            while (traversalOutputBuffer.Count == 0 && inputOp.State())
            {
                currentRecord = inputOp.Next();
                if (currentRecord == null)
                {
                    Close();
                    return null;
                }

                foreach (var traversalPair in traversalList)
                {
                    ConstantSourceOperator traversalContext = traversalPair.Item1;
                    GraphViewExecutionOperator traversal = traversalPair.Item2;
                    traversalContext.ConstantSource = currentRecord;
                    traversal.ResetState();

                    RawRecord traversalRec = null;
                    while ((traversalRec = traversal.Next()) != null)
                    {
                        traversalOutputBuffer.Enqueue(traversalRec);
                    }

                    if (traversalOutputBuffer.Count > 0)
                    {
                        break;
                    }
                }
            }

            if (traversalOutputBuffer.Count > 0)
            {
                RawRecord r = new RawRecord(currentRecord);
                RawRecord traversalRec = traversalOutputBuffer.Dequeue();
                r.Append(traversalRec);

                return r;
            }
            else
            {
                Close();
                return null;
            }
        }

        public override void ResetState()
        {
            currentRecord = null;
            inputOp.ResetState();
            traversalOutputBuffer?.Clear();
            Open();
        }
    }

    internal class RepeatOperator : GraphViewExecutionOperator
    {
        // Number of times the inner operator repeats itself.
        // If this number is less than 0, the termination condition 
        // is specified by a boolean function. 
        private int repeatTimes;

        // The termination condition of iterations
        private BooleanFunction terminationCondition;
        // If this variable is true, the iteration starts with the context record. 
        // This corresponds to the while-do loop semantics. 
        // Otherwise, the iteration starts with the the output of the first execution of the inner operator,
        // which corresponds to the do-while loop semantics.
        private bool startFromContext;
        // The condition determining whether or not an intermediate state is emitted
        private BooleanFunction emitCondition;
        // This variable specifies whether or not the context record is considered 
        // to be emitted when the iteration does not start with the context record,
        // i.e., startFromContext is false 
        private bool emitContext;

        private GraphViewExecutionOperator inputOp;
        // A list record fields (identified by field indexes) from the input 
        // operator that are fed as the initial input into the inner operator.
        private List<int> inputFieldIndexes;

        private GraphViewExecutionOperator innerOp;
        private ConstantSourceOperator innerContextOp;

        Queue<RawRecord> repeatResultBuffer;
        RawRecord currentRecord;

        public RepeatOperator(
            GraphViewExecutionOperator inputOp,
            List<int> inputFieldIndexes,
            GraphViewExecutionOperator innerOp,
            ConstantSourceOperator innerContextOp,
            int repeatTimes,
            BooleanFunction emitCondition,
            bool emitContext)
        {
            this.inputOp = inputOp;
            this.inputFieldIndexes = inputFieldIndexes;
            this.innerOp = innerOp;
            this.innerContextOp = innerContextOp;
            this.repeatTimes = repeatTimes;
            this.emitCondition = emitCondition;
            this.emitContext = emitContext;

            startFromContext = false;

            repeatResultBuffer = new Queue<RawRecord>();
            Open();
        }

        public RepeatOperator(
            GraphViewExecutionOperator inputOp,
            List<int> inputFieldIndexes,
            GraphViewExecutionOperator innerOp,
            ConstantSourceOperator innerContextOp,
            BooleanFunction terminationCondition,
            bool startFromContext,
            BooleanFunction emitCondition,
            bool emitContext)
        {
            this.inputOp = inputOp;
            this.inputFieldIndexes = inputFieldIndexes;
            this.innerOp = innerOp;
            this.innerContextOp = innerContextOp;
            this.terminationCondition = terminationCondition;
            this.startFromContext = startFromContext;
            this.emitCondition = emitCondition;
            this.emitContext = emitContext;
            this.repeatTimes = -1;

            repeatResultBuffer = new Queue<RawRecord>();
            Open();
        }

        public override RawRecord Next()
        {
            while (repeatResultBuffer.Count == 0 && inputOp.State())
            {
                currentRecord = inputOp.Next();
                if (currentRecord == null)
                {
                    Close();
                    return null;
                }

                RawRecord initialRec = new RawRecord {fieldValues = new List<FieldObject>()};
                foreach (int fieldIndex in inputFieldIndexes)
                {
                    initialRec.Append(fieldIndex != -1 ? currentRecord[fieldIndex] : null);
                }

                if (repeatTimes >= 0)
                {
                    // By current implementation of Gremlin, when repeat time is set to 0,
                    // it is reset to 1.
                    repeatTimes = repeatTimes == 0 ? 1 : repeatTimes;

                    Queue<RawRecord> priorStates = new Queue<RawRecord>();
                    Queue<RawRecord> newStates = new Queue<RawRecord>();

                    if (emitCondition != null && emitContext)
                    {
                        if (emitCondition.Evaluate(initialRec))
                        {
                            repeatResultBuffer.Enqueue(initialRec);
                        }
                    }

                    // Evaluates the loop for the first time
                    innerContextOp.ConstantSource = initialRec;
                    innerOp.ResetState();
                    RawRecord newRec = null;
                    while ((newRec = innerOp.Next()) != null)
                    {
                        priorStates.Enqueue(newRec);

                        if (emitCondition != null && emitCondition.Evaluate(newRec))
                        {
                            repeatResultBuffer.Enqueue(newRec);
                        }
                    }

                    // Evaluates the remaining number of iterations
                    for (int i = 0; i < repeatTimes - 1; i++)
                    {
                        while (priorStates.Count > 0)
                        {
                            RawRecord priorRec = priorStates.Dequeue();
                            innerContextOp.ConstantSource = priorRec;
                            innerOp.ResetState();
                            newRec = null;
                            while ((newRec = innerOp.Next()) != null)
                            {
                                newStates.Enqueue(newRec);

                                if (emitCondition != null && emitCondition.Evaluate(newRec))
                                {
                                    repeatResultBuffer.Enqueue(newRec);
                                }
                            }
                        }

                        Queue<RawRecord> tmpQueue = priorStates;
                        priorStates = newStates;
                        newStates = tmpQueue;
                    }

                    if (emitCondition == null)
                    {
                        foreach (RawRecord resultRec in priorStates)
                        {
                            repeatResultBuffer.Enqueue(resultRec);
                        }
                    }
                }
                else 
                {
                    Queue<RawRecord> states = new Queue<RawRecord>();

                    if (startFromContext)
                    {
                        if (terminationCondition != null && terminationCondition.Evaluate(initialRec))
                        {
                            repeatResultBuffer.Enqueue(initialRec);
                        }
                        else if (emitContext)
                        {
                            if (emitCondition == null || emitCondition.Evaluate(initialRec))
                            {
                                repeatResultBuffer.Enqueue(initialRec);
                            }
                        }
                    }
                    else
                    {
                        if (emitContext && emitCondition != null)
                        {
                            if (emitCondition.Evaluate(initialRec))
                            {
                                repeatResultBuffer.Enqueue(initialRec);
                            }
                        }
                    }

                    // Evaluates the loop for the first time
                    innerContextOp.ConstantSource = initialRec;
                    innerOp.ResetState();
                    RawRecord newRec = null;
                    while ((newRec = innerOp.Next()) != null)
                    {
                        states.Enqueue(newRec);
                    }

                    // Evaluates the remaining iterations
                    while (states.Count > 0)
                    {
                        RawRecord stateRec = states.Dequeue();

                        if (terminationCondition != null && terminationCondition.Evaluate(stateRec))
                        {
                            repeatResultBuffer.Enqueue(stateRec);
                        }
                        else
                        {
                            if (emitCondition != null && emitCondition.Evaluate(stateRec))
                            {
                                repeatResultBuffer.Enqueue(stateRec);
                            }

                            innerContextOp.ConstantSource = stateRec;
                            innerOp.ResetState();
                            RawRecord loopRec = null;
                            while ((loopRec = innerOp.Next()) != null)
                            {
                                states.Enqueue(loopRec);
                            }
                        }
                    }
                }
            }

            if (repeatResultBuffer.Count > 0)
            {
                RawRecord r = new RawRecord(currentRecord);
                RawRecord repeatRecord = repeatResultBuffer.Dequeue();
                r.Append(repeatRecord);

                return r;
            }
            else
            {
                Close();
                return null;
            }
        }

        public override void ResetState()
        {
            currentRecord = null;
            inputOp.ResetState();
            innerOp.ResetState();
            innerContextOp.ResetState();
            repeatResultBuffer?.Clear();
            Open();
        }
    }

    internal class DeduplicateOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator inputOp;
        private List<HashSet<Object>> compositeDedupKeySet;
        private List<ScalarFunction> compositeDedupKeyFuncList;

        internal DeduplicateOperator(GraphViewExecutionOperator inputOperator, List<ScalarFunction> compositeDedupKeyFuncList)
        {
            this.inputOp = inputOperator;
            this.compositeDedupKeyFuncList = compositeDedupKeyFuncList;
            this.compositeDedupKeySet = new List<HashSet<Object>>();
            for (int i = 0; i < compositeDedupKeyFuncList.Count; i++) {
                compositeDedupKeySet.Add(new HashSet<Object>());
            }
            this.Open();
        }

        public override RawRecord Next()
        {
            RawRecord srcRecord = null;
                
            while (this.inputOp.State() && (srcRecord = this.inputOp.Next()) != null)
            {
                bool hasNewUniqueKey = false;

                for (int dedupKeyIndex = 0; dedupKeyIndex < compositeDedupKeyFuncList.Count; dedupKeyIndex++)
                {
                    ScalarFunction getDedupKeyFunc = compositeDedupKeyFuncList[dedupKeyIndex];
                    FieldObject key = getDedupKeyFunc.Evaluate(srcRecord);
                    if (key == null) {
                        throw new GraphViewException("The provided traversal or property name of Dedup does not map to a value.");
                    }

                    if (!compositeDedupKeySet[dedupKeyIndex].Contains(key))
                    {
                        compositeDedupKeySet[dedupKeyIndex].Add(key);
                        hasNewUniqueKey = true;
                    }
                }

                if (!hasNewUniqueKey) {
                    continue;
                }

                return srcRecord;
            }

            this.Close();
            this.compositeDedupKeySet.Clear();
            return null;
        }

        public override void ResetState()
        {
            this.inputOp.ResetState();
            this.compositeDedupKeySet.Clear();
            for (int i = 0; i < this.compositeDedupKeyFuncList.Count; i++) {
                compositeDedupKeySet.Add(new HashSet<Object>());
            }
            this.Open();
        }
    }

    internal class DeduplicateLocalOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator inputOp;
        private ScalarFunction getInputObjectionFunc;

        internal DeduplicateLocalOperator(GraphViewExecutionOperator inputOperator, ScalarFunction getInputObjectionFunc)
        {
            this.inputOp = inputOperator;
            this.getInputObjectionFunc = getInputObjectionFunc;

            this.Open();
        }

        public override RawRecord Next()
        {
            RawRecord currentRecord;

            while (this.inputOp.State() && (currentRecord = this.inputOp.Next()) != null)
            {
                RawRecord result = new RawRecord(currentRecord);
                FieldObject inputObject = this.getInputObjectionFunc.Evaluate(currentRecord);

                HashSet<Object> localObjectsSet = new HashSet<Object>();

                if (!(inputObject is CollectionField))
                    throw new GraphViewException("Dedup(local) can only be applied to a list.");

                CollectionField inputCollection = (CollectionField) inputObject;

                for (int localObjectIndex = inputCollection.Collection.Count - 1; localObjectIndex >= 0; localObjectIndex--)
                {
                    Object localObj = inputCollection.Collection[localObjectIndex].ToObject();
                    if (localObjectsSet.Contains(localObj))
                    {
                        inputCollection.Collection.RemoveAt(localObjectIndex);
                        continue;
                    }

                    localObjectsSet.Add(localObj);
                }

                return result;
            }

            this.Close();
            return null;
        }

        public override void ResetState()
        {
            this.inputOp.ResetState();
            this.Open();
        }
    }

    internal class RangeOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator inputOp;
        private int startIndex;
        //
        // if count is -1, return all the records starting from startIndex
        //
        private int highEnd;
        private int index;

        internal RangeOperator(GraphViewExecutionOperator inputOp, int startIndex, int count)
        {
            this.inputOp = inputOp;
            this.startIndex = startIndex;
            this.highEnd = count == -1 ? -1 : startIndex + count;
            this.index = 0;
            this.Open();
        }

        public override RawRecord Next()
        {
            RawRecord srcRecord = null;

            //
            // Return records in the [startIndex, highEnd)
            //
            while (this.inputOp.State() && (srcRecord = this.inputOp.Next()) != null)
            {
                if (this.index < this.startIndex || (this.highEnd != -1 && this.index >= this.highEnd))
                {
                    this.index++;
                    continue;
                }

                this.index++;
                return srcRecord;
            }

            this.Close();
            return null;
        }

        public override void ResetState()
        {
            this.inputOp.ResetState();
            this.index = 0;
            this.Open();
        }
    }

    internal class RangeLocalOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator inputOp;
        private int startIndex;
        //
        // if count is -1, return all the records starting from startIndex
        //
        private int count;
        private int inputCollectionIndex;

        internal RangeLocalOperator(GraphViewExecutionOperator inputOp, int inputCollectionIndex, int startIndex, int count)
        {
            this.inputOp = inputOp;
            this.startIndex = startIndex;
            this.count = count;
            this.inputCollectionIndex = inputCollectionIndex;
            this.Open();
        }

        public override RawRecord Next()
        {
            RawRecord srcRecord = null;

            while (this.inputOp.State() && (srcRecord = this.inputOp.Next()) != null)
            {
                //
                // Return records in the [runtimeStartIndex, runtimeStartIndex + runtimeCount)
                //
                FieldObject inputObject = srcRecord[inputCollectionIndex];
                if (inputObject is CollectionField)
                {
                    CollectionField inputCollection = inputObject as CollectionField;

                    int runtimeStartIndex = startIndex > inputCollection.Collection.Count ? inputCollection.Collection.Count : startIndex;
                    int runtimeCount = this.count == -1 ? inputCollection.Collection.Count - runtimeStartIndex : this.count;
                    if (runtimeStartIndex + runtimeCount > inputCollection.Collection.Count) {
                        runtimeCount = inputCollection.Collection.Count - runtimeStartIndex;
                    }

                    inputCollection.Collection = inputCollection.Collection.GetRange(runtimeStartIndex, runtimeCount);
                }
                //
                // Return records in the [low, high)
                //
                else if (inputObject is MapField)
                {
                    MapField inputMap = inputObject as MapField;
                    List<FieldObject> order = inputMap.Order;
                    int low = startIndex;
                    int high = this.count == -1 ? order.Count : low + this.count;

                    int index = order.Count - 1;
                    for (; index >= low; index--)
                    {
                        if (index >= high) {
                            inputMap.RemoveAt(index);
                        }
                    }
                    while (index >= 0) {
                        inputMap.RemoveAt(index--);
                    }
                }

                return srcRecord;
            }

            this.Close();
            return null;
        }

        public override void ResetState()
        {
            this.inputOp.ResetState();
            this.Open();
        }
    }

    internal class TailOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator inputOp;
        private int lastN;
        private int count;
        private List<RawRecord> buffer; 

        internal TailOperator(GraphViewExecutionOperator inputOp, int lastN)
        {
            this.inputOp = inputOp;
            this.lastN = lastN;
            this.count = 0;
            this.buffer = new List<RawRecord>();

            this.Open();
        }

        public override RawRecord Next()
        {
            RawRecord srcRecord = null;

            while (this.inputOp.State() && (srcRecord = this.inputOp.Next()) != null) {
                buffer.Add(srcRecord);
            }

            //
            // Reutn records from [buffer.Count - lastN, buffer.Count)
            //

            int startIndex = buffer.Count < lastN ? 0 : buffer.Count - lastN;
            int index = startIndex + this.count++;
            while (index < buffer.Count) {
                return buffer[index];
            } 

            this.Close();
            this.buffer.Clear();
            return null;
        }

        public override void ResetState()
        {
            this.inputOp.ResetState();
            this.count = 0;
            this.buffer.Clear();
            this.Open();
        }
    }

    internal class TailLocalOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator inputOp;
        private int lastN;
        private int inputCollectionIndex;

        internal TailLocalOperator(GraphViewExecutionOperator inputOp, int inputCollectionIndex, int lastN)
        {
            this.inputOp = inputOp;
            this.inputCollectionIndex = inputCollectionIndex;
            this.lastN = lastN;

            this.Open();
        }

        public override RawRecord Next()
        {
            RawRecord srcRecord = null;

            while (this.inputOp.State() && (srcRecord = this.inputOp.Next()) != null)
            {
                //
                // Return records in the [localCollection.Count - lastN, localCollection.Count)
                //
                FieldObject inputObject = srcRecord[inputCollectionIndex];
                if (inputObject is CollectionField)
                {
                    CollectionField inputCollection = inputObject as CollectionField;

                    int startIndex = inputCollection.Collection.Count < lastN 
                                     ? 0 
                                     : inputCollection.Collection.Count - lastN;
                    int count = startIndex + lastN > inputCollection.Collection.Count
                                     ? inputCollection.Collection.Count - startIndex
                                     : lastN;
                    inputCollection.Collection = inputCollection.Collection.GetRange(startIndex, count);
                }
                //
                // Return records in the [low, inputMap.Count)
                //
                else if (inputObject is MapField)
                {
                    MapField inputMap = inputObject as MapField;
                    int low = inputMap.Count - lastN;

                    int index = low - 1;
                    while (index >= 0) {
                        inputMap.RemoveAt(index--);
                    }
                }

                return srcRecord;
            }

            this.Close();
            return null;
        }

        public override void ResetState()
        {
            this.inputOp.ResetState();
            this.Open();
        }
    }

    internal class SideEffectOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator inputOp;

        private GraphViewExecutionOperator sideEffectTraversal;
        private ConstantSourceOperator contextOp;

        public SideEffectOperator(
            GraphViewExecutionOperator inputOp,
            GraphViewExecutionOperator sideEffectTraversal,
            ConstantSourceOperator contextOp)
        {
            this.inputOp = inputOp;
            this.sideEffectTraversal = sideEffectTraversal;
            this.contextOp = contextOp;

            Open();
        }

        public override RawRecord Next()
        {
            while (inputOp.State())
            {
                RawRecord currentRecord = inputOp.Next();
                if (currentRecord == null)
                {
                    Close();
                    return null;
                }

                //RawRecord resultRecord = new RawRecord(currentRecord);
                contextOp.ConstantSource = currentRecord;
                sideEffectTraversal.ResetState();

                while (sideEffectTraversal.State())
                {
                    sideEffectTraversal.Next();
                }

                return currentRecord;
            }

            Close();
            return null;
        }

        public override void ResetState()
        {
            inputOp.ResetState();
            contextOp.ResetState();
            sideEffectTraversal.ResetState();
            Open();
        }
    }

    internal class InjectOperator : GraphViewExecutionOperator
    {
        GraphViewExecutionOperator inputOp;

        // The number of columns returned by each subquery equals to inputIndexes.Count
        List<GraphViewExecutionOperator> subqueries;
        int subqueryProgress;
       
        public InjectOperator(
            List<GraphViewExecutionOperator> subqueries, 
            GraphViewExecutionOperator inputOp)
        {
            this.subqueries = subqueries;
            this.inputOp = inputOp;
            subqueryProgress = 0;
            Open();
        }

        public override RawRecord Next()
        {
            RawRecord r = null;

            while (subqueryProgress < subqueries.Count)
            {
                r = subqueries[subqueryProgress].Next();
                if (r != null)
                {
                    return r;
                }

                subqueryProgress++;
            }

            // For the g.Inject() case, Inject operator itself is the first operator, and its inputOp is null
            if (inputOp != null)
                r = inputOp.State() ? inputOp.Next() : null;

            if (r == null)
            {
                Close();
            }

            return r;
        }

        public override void ResetState()
        {
            foreach (GraphViewExecutionOperator subqueryOp in subqueries)
            {
                subqueryOp.ResetState();
            }

            subqueryProgress = 0;
            Open();
        }
    }

    internal class AggregateOperator : GraphViewExecutionOperator
    {
        CollectionFunction aggregateState;
        GraphViewExecutionOperator inputOp;
        ScalarFunction getAggregateObjectFunction;
        Queue<RawRecord> outputBuffer;

        public AggregateOperator(GraphViewExecutionOperator inputOp, ScalarFunction getTargetFieldFunction, CollectionFunction aggregateState)
        {
            this.aggregateState = aggregateState;
            this.inputOp = inputOp;
            this.getAggregateObjectFunction = getTargetFieldFunction;
            this.outputBuffer = new Queue<RawRecord>();

            Open();
        }

        public override RawRecord Next()
        {
            RawRecord r = null;
            while (inputOp.State() && (r = inputOp.Next()) != null)
            {
                RawRecord result = new RawRecord(r);

                FieldObject aggregateObject = getAggregateObjectFunction.Evaluate(r);

                if (aggregateObject == null)
                    throw new GraphViewException("The provided traversal or property name in Aggregate does not map to a value.");

                aggregateState.Accumulate(aggregateObject);

                result.Append(aggregateState.CollectionField);

                outputBuffer.Enqueue(result);
            }

            if (outputBuffer.Count <= 1) Close();
            if (outputBuffer.Count != 0) return outputBuffer.Dequeue();
            return null;
        }

        public override void ResetState()
        {
            //aggregateState.Init();
            inputOp.ResetState();
            Open();
        }
    }

    internal class StoreOperator : GraphViewExecutionOperator
    {
        CollectionFunction storeState;
        GraphViewExecutionOperator inputOp;
        ScalarFunction getStoreObjectFunction;

        public StoreOperator(GraphViewExecutionOperator inputOp, ScalarFunction getTargetFieldFunction, CollectionFunction storeState)
        {
            this.storeState = storeState;
            this.inputOp = inputOp;
            this.getStoreObjectFunction = getTargetFieldFunction;
            Open();
        }

        public override RawRecord Next()
        {
            if (inputOp.State())
            {
                RawRecord r = inputOp.Next();
                if (r == null)
                {
                    Close();
                    return null;
                }

                RawRecord result = new RawRecord(r);

                FieldObject storeObject = getStoreObjectFunction.Evaluate(r);

                if (storeObject == null)
                    throw new GraphViewException("The provided traversal or property name in Store does not map to a value.");

                storeState.Accumulate(storeObject);

                result.Append(storeState.CollectionField);

                if (!inputOp.State())
                {
                    Close();
                }
                return result;
            }

            return null;
        }

        public override void ResetState()
        {
            //storeState.Init();
            inputOp.ResetState();
            Open();
        }
    }


    //
    // Note: our BarrierOperator's semantics is not the same the one's in Gremlin
    //
    internal class BarrierOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator _inputOp;
        private Queue<RawRecord> _outputBuffer;
        private int _outputBufferSize;

        public BarrierOperator(GraphViewExecutionOperator inputOp, int outputBufferSize = -1)
        {
            _inputOp = inputOp;
            _outputBuffer = new Queue<RawRecord>();
            _outputBufferSize = outputBufferSize;
            Open();
        }
          
        public override RawRecord Next()
        {
            while (_outputBuffer.Any()) {
                return _outputBuffer.Dequeue();
            }

            RawRecord record;
            while ((_outputBufferSize == -1 || _outputBuffer.Count <= _outputBufferSize) 
                    && _inputOp.State() 
                    && (record = _inputOp.Next()) != null)
            {
                _outputBuffer.Enqueue(record);
            }

            if (_outputBuffer.Count <= 1) Close();
            if (_outputBuffer.Count != 0) return _outputBuffer.Dequeue();
            return null;
        }

        public override void ResetState()
        {
            _inputOp.ResetState();
            _outputBuffer.Clear();
            Open();
        }
    }

    internal class ProjectByOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator _inputOp;
        private List<Tuple<ConstantSourceOperator, GraphViewExecutionOperator, string>> _projectList;

        internal ProjectByOperator(GraphViewExecutionOperator pInputOperator)
        {
            _inputOp = pInputOperator;
            _projectList = new List<Tuple<ConstantSourceOperator, GraphViewExecutionOperator, string>>();
            Open();
        }

        public void AddProjectBy(ConstantSourceOperator contextOp, GraphViewExecutionOperator traversal, string key)
        {
            _projectList.Add(new Tuple<ConstantSourceOperator, GraphViewExecutionOperator, string>(contextOp, traversal, key));
        }

        public override RawRecord Next()
        {
            RawRecord currentRecord;

            while (_inputOp.State() && (currentRecord = _inputOp.Next()) != null)
            {
                MapField projectMap = new MapField();
                RawRecord extraRecord = new RawRecord();

                foreach (var tuple in _projectList)
                {
                    string projectKey = tuple.Item3;
                    ConstantSourceOperator projectContext = tuple.Item1;
                    GraphViewExecutionOperator projectTraversal = tuple.Item2;
                    projectContext.ConstantSource = currentRecord;
                    projectTraversal.ResetState();

                    RawRecord projectRec = projectTraversal.Next();
                    projectTraversal.Close();

                    if (projectRec == null)
                        throw new GraphViewException(
                            string.Format("The provided traverser of key \"{0}\" does not map to a value.", projectKey));

                    projectMap.Add(new StringField(projectKey), projectRec.RetriveData(0));
                    for (var i = 1; i < projectRec.Length; i++)
                        extraRecord.Append(projectRec[i]);
                }

                var result = new RawRecord(currentRecord);
                result.Append(projectMap);
                if (extraRecord.Length > 0)
                    result.Append(extraRecord);

                return result;
            }

            Close();
            return null;
        }

        public override void ResetState()
        {
            _inputOp.ResetState();
            Open();
        }
    }

    internal class PropertyKeyOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator inputOp;
        private int propertyFieldIndex;

        public PropertyKeyOperator(GraphViewExecutionOperator inputOp, int propertyFieldIndex)
        {
            this.inputOp = inputOp;
            this.propertyFieldIndex = propertyFieldIndex;
            this.Open();
        }


        public override RawRecord Next()
        {
            RawRecord currentRecord;

            while (inputOp.State() && (currentRecord = inputOp.Next()) != null)
            {
                PropertyField p = currentRecord[this.propertyFieldIndex] as PropertyField;
                if (p == null)
                    continue;

                RawRecord result = new RawRecord(currentRecord);
                result.Append(new StringField(p.PropertyName));

                return result;
            }

            Close();
            return null;
        }

        public override void ResetState()
        {
            this.inputOp.ResetState();
            this.Open();
        }
    }

    internal class PropertyValueOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator inputOp;
        private int propertyFieldIndex;

        public PropertyValueOperator(GraphViewExecutionOperator inputOp, int propertyFieldIndex)
        {
            this.inputOp = inputOp;
            this.propertyFieldIndex = propertyFieldIndex;
            this.Open();
        }

        public override RawRecord Next()
        {
            RawRecord currentRecord;

            while (inputOp.State() && (currentRecord = inputOp.Next()) != null)
            {
                PropertyField p = currentRecord[this.propertyFieldIndex] as PropertyField;
                if (p == null)
                    continue;

                RawRecord result = new RawRecord(currentRecord);
                result.Append(new StringField(p.PropertyValue, p.JsonDataType));

                return result;
            }

            Close();
            return null;
        }

        public override void ResetState()
        {
            this.inputOp.ResetState();
            this.Open();
        }
    }

    internal class QueryDerivedTableOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator _queryOp;
        private ContainerOperator _rootContainerOp;

        public QueryDerivedTableOperator(GraphViewExecutionOperator queryOp, ContainerOperator containerOp)
        {
            _queryOp = queryOp;
            _rootContainerOp = containerOp;

            Open();
        }

        public override RawRecord Next()
        {
            RawRecord derivedRecord;

            while (_queryOp.State() && (derivedRecord = _queryOp.Next()) != null)
            {
                return derivedRecord;
            }

            Close();
            return null;
        }

        public override void ResetState()
        {
            _queryOp.ResetState();
            _rootContainerOp?.ResetState();

            Open();
        }
    }

    internal class CountLocalOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator _inputOp;
        private int _objectIndex;

        public CountLocalOperator(GraphViewExecutionOperator inputOp, int objectIndex)
        {
            _inputOp = inputOp;
            _objectIndex = objectIndex;
            Open();
        }

        public override RawRecord Next()
        {
            RawRecord currentRecord;

            while (_inputOp.State() && (currentRecord = _inputOp.Next()) != null)
            {
                RawRecord result = new RawRecord(currentRecord);
                FieldObject obj = currentRecord[_objectIndex];
                Debug.Assert(obj != null, "The input of the CountLocalOperator should not be null.");

                if (obj is CollectionField)
                    result.Append(new StringField(((CollectionField)obj).Collection.Count.ToString(), JsonDataType.Long));
                else if (obj is MapField)
                    result.Append(new StringField(((MapField)obj).Count.ToString(), JsonDataType.Long));
                else if (obj is TreeField)
                    result.Append(new StringField(((TreeField)obj).Children.Count.ToString(), JsonDataType.Long));
                else
                    result.Append(new StringField("1", JsonDataType.Int));

                return result;
            }

            Close();
            return null;
        }

        public override void ResetState()
        {
            _inputOp.ResetState();
            Open();
        }
    }

    internal class SumLocalOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator _inputOp;
        private int _objectIndex;

        public SumLocalOperator(GraphViewExecutionOperator inputOp, int objectIndex)
        {
            _inputOp = inputOp;
            _objectIndex = objectIndex;
            Open();
        }

        public override RawRecord Next()
        {
            RawRecord currentRecord;

            while (_inputOp.State() && (currentRecord = _inputOp.Next()) != null)
            {
                FieldObject obj = currentRecord[_objectIndex];
                Debug.Assert(obj != null, "The input of the SumLocalOperator should not be null.");

                double sum = 0.0;
                double current;

                if (obj is CollectionField)
                {
                    foreach (FieldObject fieldObject in ((CollectionField)obj).Collection)
                    {
                        if (!double.TryParse(fieldObject.ToValue, out current))
                            throw new GraphViewException("The element of the local object cannot be cast to a number");

                        sum += current;
                    }
                }
                else {
                    sum = double.TryParse(obj.ToValue, out current) ? current : double.NaN;
                }

                RawRecord result = new RawRecord(currentRecord);
                result.Append(new StringField(sum.ToString(CultureInfo.InvariantCulture), JsonDataType.Double));

                return result;
            }

            Close();
            return null;
        }

        public override void ResetState()
        {
            _inputOp.ResetState();
            Open();
        }
    }

    internal class MaxLocalOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator _inputOp;
        private int _objectIndex;

        public MaxLocalOperator(GraphViewExecutionOperator inputOp, int objectIndex)
        {
            _inputOp = inputOp;
            _objectIndex = objectIndex;
            Open();
        }

        public override RawRecord Next()
        {
            RawRecord currentRecord;

            while (_inputOp.State() && (currentRecord = _inputOp.Next()) != null)
            {
                FieldObject obj = currentRecord[_objectIndex];
                Debug.Assert(obj != null, "The input of the MaxLocalOperator should not be null.");

                double max = double.MinValue;
                double current;

                if (obj is CollectionField)
                {
                    foreach (FieldObject fieldObject in ((CollectionField)obj).Collection)
                    {
                        if (!double.TryParse(fieldObject.ToValue, out current))
                            throw new GraphViewException("The element of the local object cannot be cast to a number");

                        if (max < current)
                            max = current;
                    }
                }
                else {
                    max = double.TryParse(obj.ToValue, out current) ? current : double.NaN;
                }

                RawRecord result = new RawRecord(currentRecord);
                result.Append(new StringField(max.ToString(CultureInfo.InvariantCulture), JsonDataType.Double));

                return result;
            }

            Close();
            return null;
        }

        public override void ResetState()
        {
            _inputOp.ResetState();
            Open();
        }
    }

    internal class MinLocalOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator _inputOp;
        private int _objectIndex;

        public MinLocalOperator(GraphViewExecutionOperator inputOp, int objectIndex)
        {
            _inputOp = inputOp;
            _objectIndex = objectIndex;
            Open();
        }

        public override RawRecord Next()
        {
            RawRecord currentRecord;

            while (_inputOp.State() && (currentRecord = _inputOp.Next()) != null)
            {
                FieldObject obj = currentRecord[_objectIndex];
                Debug.Assert(obj != null, "The input of the MinLocalOperator should not be null.");

                double min = double.MaxValue;
                double current;

                if (obj is CollectionField)
                {
                    foreach (FieldObject fieldObject in ((CollectionField)obj).Collection)
                    {
                        if (!double.TryParse(fieldObject.ToValue, out current))
                            throw new GraphViewException("The element of the local object cannot be cast to a number");

                        if (current < min)
                            min = current;
                    }
                }
                else {
                    min = double.TryParse(obj.ToValue, out current) ? current : double.NaN;
                }

                RawRecord result = new RawRecord(currentRecord);
                result.Append(new StringField(min.ToString(CultureInfo.InvariantCulture), JsonDataType.Double));

                return result;
            }

            Close();
            return null;
        }

        public override void ResetState()
        {
            _inputOp.ResetState();
            Open();
        }
    }

    internal class MeanLocalOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator _inputOp;
        private int _objectIndex;

        public MeanLocalOperator(GraphViewExecutionOperator inputOp, int objectIndex)
        {
            _inputOp = inputOp;
            _objectIndex = objectIndex;
            Open();
        }

        public override RawRecord Next()
        {
            RawRecord currentRecord;

            while (_inputOp.State() && (currentRecord = _inputOp.Next()) != null)
            {
                FieldObject obj = currentRecord[_objectIndex];
                Debug.Assert(obj != null, "The input of the MeanLocalOperator should not be null.");

                double sum = 0.0;
                long count = 0;
                double current;

                if (obj is CollectionField)
                {
                    foreach (FieldObject fieldObject in ((CollectionField)obj).Collection)
                    {
                        if (!double.TryParse(fieldObject.ToValue, out current))
                            throw new GraphViewException("The element of the local object cannot be cast to a number");

                        sum += current;
                        count++;
                    }
                }
                else
                {
                    count = 1;
                    sum = double.TryParse(obj.ToValue, out current) ? current : double.NaN;
                }

                RawRecord result = new RawRecord(currentRecord);
                result.Append(new StringField((sum / count).ToString(CultureInfo.InvariantCulture), JsonDataType.Double));

                return result;
            }

            Close();
            return null;
        }

        public override void ResetState()
        {
            _inputOp.ResetState();
            Open();
        }
    }

    internal class SimplePathOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator inputOp;
        private int pathIndex;
        private HashSet<FieldObject> intermediateStepSet;

        public SimplePathOperator(GraphViewExecutionOperator inputOp, int pathIndex)
        {
            this.inputOp = inputOp;
            this.pathIndex = pathIndex;
            this.intermediateStepSet = new HashSet<FieldObject>();
            this.Open();
        }

        public override RawRecord Next()
        {
            RawRecord currentRecord;

            while (this.inputOp.State() && (currentRecord = this.inputOp.Next()) != null)
            {
                RawRecord result = new RawRecord(currentRecord);
                CollectionField path = currentRecord[pathIndex] as CollectionField;

                Debug.Assert(path != null, "The input of the simplePath filter should be a CollectionField generated by path().");

                bool isSimplePath = true;
                foreach (FieldObject step in path.Collection)
                {
                    if (intermediateStepSet.Contains(step))
                    {
                        isSimplePath = false;
                        break;
                    }
                        
                    intermediateStepSet.Add(step);
                }

                if (isSimplePath) {
                    return result;
                }
            }

            this.Close();
            return null;
        }

        public override void ResetState()
        {
            this.inputOp.ResetState();
            this.intermediateStepSet.Clear();
            this.Open();
        }
    }

    internal class CyclicPathOperator : GraphViewExecutionOperator
    {
        private GraphViewExecutionOperator inputOp;
        private int pathIndex;
        private HashSet<FieldObject> intermediateStepSet;

        public CyclicPathOperator(GraphViewExecutionOperator inputOp, int pathIndex)
        {
            this.inputOp = inputOp;
            this.pathIndex = pathIndex;
            this.intermediateStepSet = new HashSet<FieldObject>();
            this.Open();
        }

        public override RawRecord Next()
        {
            RawRecord currentRecord;

            while (this.inputOp.State() && (currentRecord = this.inputOp.Next()) != null)
            {
                RawRecord result = new RawRecord(currentRecord);
                CollectionField path = currentRecord[pathIndex] as CollectionField;

                Debug.Assert(path != null, "The input of the cyclicPath filter should be a CollectionField generated by path().");

                bool isCyclicPath = false;
                foreach (FieldObject step in path.Collection)
                {
                    if (intermediateStepSet.Contains(step))
                    {
                        isCyclicPath = true;
                        break;
                    }

                    intermediateStepSet.Add(step);
                }

                if (isCyclicPath) {
                    return result;
                }
            }

            this.Close();
            return null;
        }

        public override void ResetState()
        {
            this.inputOp.ResetState();
            this.intermediateStepSet.Clear();
            this.Open();
        }
    }

    internal class ChooseOperator : GraphViewExecutionOperator
    {
        GraphViewExecutionOperator inputOp;

        ScalarFunction scalarSubQueryFunc;

        ConstantSourceOperator tempSourceOp;
        ContainerOperator trueBranchSourceOp;
        ContainerOperator falseBranchSourceOp;

        Queue<RawRecord> evaluatedTrueRecords;
        Queue<RawRecord> evaluatedFalseRecords;

        GraphViewExecutionOperator trueBranchTraversalOp;
        GraphViewExecutionOperator falseBranchTraversalOp;

        public ChooseOperator(
            GraphViewExecutionOperator inputOp,
            ScalarFunction scalarSubQueryFunc,
            ConstantSourceOperator tempSourceOp,
            ContainerOperator trueBranchSourceOp,
            GraphViewExecutionOperator trueBranchTraversalOp,
            ContainerOperator falseBranchSourceOp,
            GraphViewExecutionOperator falseBranchTraversalOp
            )
        {
            this.inputOp = inputOp;
            this.scalarSubQueryFunc = scalarSubQueryFunc;
            this.tempSourceOp = tempSourceOp;
            this.trueBranchSourceOp = trueBranchSourceOp;
            this.trueBranchTraversalOp = trueBranchTraversalOp;
            this.falseBranchSourceOp = falseBranchSourceOp;
            this.falseBranchTraversalOp = falseBranchTraversalOp;

            this.evaluatedTrueRecords = new Queue<RawRecord>();
            this.evaluatedFalseRecords = new Queue<RawRecord>();

            Open();
        }

        public override RawRecord Next()
        {
            RawRecord currentRecord = null;
            while (this.inputOp.State() && (currentRecord = this.inputOp.Next()) != null)
            {
                if (this.scalarSubQueryFunc.Evaluate(currentRecord) != null)
                    this.evaluatedTrueRecords.Enqueue(currentRecord);
                else
                    this.evaluatedFalseRecords.Enqueue(currentRecord);
            }

            while (this.evaluatedTrueRecords.Any())
            {
                this.tempSourceOp.ConstantSource = this.evaluatedTrueRecords.Dequeue();
                this.trueBranchSourceOp.Next();
            }

            RawRecord trueBranchTraversalRecord;
            while (this.trueBranchTraversalOp.State() && (trueBranchTraversalRecord = this.trueBranchTraversalOp.Next()) != null) {
                return trueBranchTraversalRecord;
            }

            while (this.evaluatedFalseRecords.Any())
            {
                this.tempSourceOp.ConstantSource = this.evaluatedTrueRecords.Dequeue();
                this.falseBranchSourceOp.Next();
            }

            RawRecord falseBranchTraversalRecord;
            while (this.falseBranchTraversalOp.State() && (falseBranchTraversalRecord = this.falseBranchTraversalOp.Next()) != null) {
                return falseBranchTraversalRecord;
            }

            this.Close();
            return null;
        }

        public override void ResetState()
        {
            this.inputOp.ResetState();
            this.evaluatedTrueRecords.Clear();
            this.evaluatedFalseRecords.Clear();
            this.trueBranchSourceOp.ResetState();
            this.falseBranchSourceOp.ResetState();
            this.trueBranchTraversalOp.ResetState();
            this.falseBranchTraversalOp.ResetState();

            this.Open();
        }
    }

    internal class ChooseWithOptionsOperator : GraphViewExecutionOperator
    {
        GraphViewExecutionOperator inputOp;

        ScalarFunction scalarSubQueryFunc;

        ConstantSourceOperator tempSourceOp;
        ContainerOperator optionSourceOp;

        int activeOptionTraversalIndex;
        bool needsOptionSourceInit;
        List<Tuple<object, Queue<RawRecord>, GraphViewExecutionOperator>> traversalList;

        Queue<RawRecord> noneRawRecords;
        GraphViewExecutionOperator optionNoneTraversalOp;
        const int noneBranchIndex = -1;

        public ChooseWithOptionsOperator(
            GraphViewExecutionOperator inputOp,
            ScalarFunction scalarSubQueryFunc,
            ConstantSourceOperator tempSourceOp,
            ContainerOperator optionSourceOp,
            GraphViewExecutionOperator optionNoneTraversalOp
            )
        {
            this.inputOp = inputOp;
            this.scalarSubQueryFunc = scalarSubQueryFunc;
            this.tempSourceOp = tempSourceOp;
            this.optionSourceOp = optionSourceOp;
            this.activeOptionTraversalIndex = 0;
            this.noneRawRecords = new Queue<RawRecord>();
            this.optionNoneTraversalOp = optionNoneTraversalOp;
            this.needsOptionSourceInit = true;

            this.Open();
        }

        public void AddOptionTraversal(object value, GraphViewExecutionOperator optionTraversalOp)
        {
            this.traversalList.Add(new Tuple<object, Queue<RawRecord>, GraphViewExecutionOperator>(value,
                new Queue<RawRecord>(), optionTraversalOp));
        }

        private void PrepareOptionTraversalSource(int index)
        {
            this.optionSourceOp.ResetState();
            Queue<RawRecord> chosenRecords = index != ChooseWithOptionsOperator.noneBranchIndex 
                                             ? this.traversalList[index].Item2 
                                             : this.noneRawRecords;
            while (chosenRecords.Any())
            {
                this.tempSourceOp.ConstantSource = chosenRecords.Dequeue();
                this.optionSourceOp.Next();
            }
        }

        public override RawRecord Next()
        {
            RawRecord currentRecord = null;
            while (this.inputOp.State() && (currentRecord = this.inputOp.Next()) != null)
            {
                FieldObject evaluatedValue = this.scalarSubQueryFunc.Evaluate(currentRecord);
                if (evaluatedValue == null) {
                    throw new GraphViewException("The provided traversal of choose() does not map to a value.");
                }

                bool hasBeenChosen = false;
                foreach (Tuple<object, Queue<RawRecord>, GraphViewExecutionOperator> tuple in this.traversalList)
                {
                    if (evaluatedValue.ToObject().Equals(tuple.Item1))
                    {
                        tuple.Item2.Enqueue(currentRecord);
                        hasBeenChosen = true;
                        break;
                    }
                }

                if (!hasBeenChosen && this.optionNoneTraversalOp != null) {
                    this.noneRawRecords.Enqueue(currentRecord);
                }
            }

            RawRecord traversalRecord = null;
            while (this.activeOptionTraversalIndex < this.traversalList.Count)
            {
                if (this.needsOptionSourceInit)
                {
                    this.PrepareOptionTraversalSource(this.activeOptionTraversalIndex);
                    this.needsOptionSourceInit = false;
                }

                GraphViewExecutionOperator optionTraversalOp = this.traversalList[this.activeOptionTraversalIndex].Item3;
                
                while (optionTraversalOp.State() && (traversalRecord = optionTraversalOp.Next()) != null) {
                    return traversalRecord;
                }

                this.activeOptionTraversalIndex++;
                this.needsOptionSourceInit = true;
            }

            if (this.optionNoneTraversalOp != null)
            {
                if (this.needsOptionSourceInit)
                {
                    this.PrepareOptionTraversalSource(ChooseWithOptionsOperator.noneBranchIndex);
                    this.needsOptionSourceInit = false;
                }

                while (this.optionNoneTraversalOp.State() && (traversalRecord = this.optionNoneTraversalOp.Next()) != null) {
                    return traversalRecord;
                }
            }

            this.Close();
            return null;
        }

        public override void ResetState()
        {
            this.inputOp.ResetState();
            this.optionSourceOp.ResetState();
            this.needsOptionSourceInit = true;
            this.activeOptionTraversalIndex = 0;
            this.noneRawRecords.Clear();
            this.optionNoneTraversalOp?.ResetState();

            foreach (Tuple<object, Queue<RawRecord>, GraphViewExecutionOperator> tuple in this.traversalList)
            {
                tuple.Item2.Clear();
                tuple.Item3.ResetState();
            }

            this.Open();
        }
    }


    internal class CoinOperator : GraphViewExecutionOperator
    {
        private readonly double _probability;
        private readonly GraphViewExecutionOperator _inputOp;
        private readonly Random _random;

        public CoinOperator(
            GraphViewExecutionOperator inputOp,
            double probability)
        {
            this._inputOp = inputOp;
            this._probability = probability;
            this._random = new Random();

            Open();
        }

        public override RawRecord Next()
        {
            RawRecord current = null;
            while (this._inputOp.State() && (current = this._inputOp.Next()) != null) {
                if (this._random.NextDouble() <= this._probability) {
                    return current;
                }
            }

            Close();
            return null;
        }

        public override void ResetState()
        {
            this._inputOp.ResetState();
            Open();
        }
    }

    internal class SampleOperator : GraphViewExecutionOperator
    {
        private readonly GraphViewExecutionOperator _inputOp;
        private readonly long _amountToSample;
        private readonly ScalarFunction _byFunction;  // Can be null if no "by" step
        private readonly Random _random;

        private readonly List<RawRecord> _inputRecords;
        private readonly List<double> _inputProperties;
        private int _nextIndex;

        public SampleOperator(
            GraphViewExecutionOperator inputOp,
            long amoutToSample,
            ScalarFunction byFunction)
        {
            this._inputOp = inputOp;
            this._amountToSample = amoutToSample;
            this._byFunction = byFunction;  // Can be null if no "by" step
            this._random = new Random();

            this._inputRecords = new List<RawRecord>();
            this._inputProperties = new List<double>();
            this._nextIndex = 0;
            Open();
        }

        public override RawRecord Next()
        {
            if (this._nextIndex == 0) {
                while (this._inputOp.State()) {
                    RawRecord current = this._inputOp.Next();
                    if (current == null) break;

                    this._inputRecords.Add(current);
                    if (this._byFunction != null) {
                        this._inputProperties.Add(double.Parse(this._byFunction.Evaluate(current).ToValue));
                    }
                }
            }

            // Return nothing if sample amount <= 0
            if (this._amountToSample <= 0) {
                Close();
                return null;
            }

            // Return all if sample amount > amount of inputs
            if (this._amountToSample >= this._inputRecords.Count) {
                if (this._nextIndex == this._inputRecords.Count - 1) {
                    Close();
                }
                return this._inputRecords[this._nextIndex++];
            }

            // Sample!
            if (this._nextIndex < this._amountToSample) {
                
                // TODO: Implement the sampling algorithm!
                return this._inputRecords[this._nextIndex++];
            }

            Close();
            return null;
        }

        public override void ResetState()
        {
            this._inputOp.ResetState();

            this._inputRecords.Clear();
            this._inputProperties.Clear();
            this._nextIndex = 0;
            Open();
        }
    }

    internal class Decompose1Operator : GraphViewExecutionOperator
    {
        GraphViewExecutionOperator inputOp;
        int decomposeTargetIndex;
        List<string> populateColumns;

        public Decompose1Operator(
            GraphViewExecutionOperator inputOp,
            int decomposeTargetIndex,
            List<string> populateColumns)
        {
            this.inputOp = inputOp;
            this.decomposeTargetIndex = decomposeTargetIndex;
            this.populateColumns = populateColumns;

            this.Open();
        }

        public override RawRecord Next()
        {
            RawRecord inputRecord = null;
            while (this.inputOp.State() && (inputRecord = this.inputOp.Next()) != null)
            {
                Compose1Field compose1Obj = inputRecord[this.decomposeTargetIndex] as Compose1Field;
                Debug.Assert(compose1Obj != null, "compose1Obj != null");

                RawRecord r = new RawRecord(inputRecord);

                foreach (string populateColumn in this.populateColumns) {
                    r.Append(compose1Obj[populateColumn]);
                }

                return r;
            }

            Close();
            return null;
        }

        public override void ResetState()
        {
            this.inputOp.ResetState();
            this.Open();
        }
    }
}
