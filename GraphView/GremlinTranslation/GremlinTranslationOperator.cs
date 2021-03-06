﻿using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace GraphView
{
    internal abstract class GremlinTranslationOperator: IGremlinByModulating
    {
        public GremlinTranslationOperator InputOperator { get; set; }

        internal virtual GremlinToSqlContext GetContext()
        {
            return null;
        }

        internal virtual WSqlScript ToSqlScript() {
            return GetContext().ToSqlScript();
        }

        internal virtual void InheritedVariableFromParent(GremlinToSqlContext parentContext)
        {
            if (this is GremlinParentContextOp)
            {
                GremlinParentContextOp rootAsContextOp = this as GremlinParentContextOp;
                rootAsContextOp.InheritedPivotVariable = parentContext.PivotVariable;
                //rootAsContextOp.InheritedPathList = new List<GremlinMatchPath>(parentContext.PathList);
                rootAsContextOp.ParentContext = parentContext;
            }
        }

        internal virtual void InheritedContextFromParent(GremlinToSqlContext parentContext)
        {
            if (this is GremlinParentContextOp)
            {
                GremlinParentContextOp rootAsContextOp = this as GremlinParentContextOp;
                rootAsContextOp.InheritedContext = parentContext.Duplicate();
            }
        }

        internal GremlinToSqlContext GetInputContext()
        {
            return InputOperator != null ? InputOperator.GetContext() : new GremlinToSqlContext();
        }

        public virtual void ModulateBy()
        {
            throw new NotImplementedException();
        }

        public virtual void ModulateBy(GraphTraversal2 traversal)
        {
            throw new NotImplementedException();
        }

        public virtual void ModulateBy(string key)
        {
            throw new NotImplementedException();
        }

        public virtual void ModulateBy(GremlinKeyword.Order order)
        {
            throw new NotImplementedException();
        }

        public virtual void ModulateBy(IComparer order)
        {
            throw new NotImplementedException();
        }

        public virtual void ModulateBy(string key, GremlinKeyword.Order order)
        {
            throw new NotImplementedException();
        }

        public virtual void ModulateBy(GraphTraversal2 traversal, GremlinKeyword.Order order)
        {
            throw new NotImplementedException();
        }

        public virtual void ModulateBy(string key, IComparer order)
        {
            throw new NotImplementedException();
        }

        public virtual void ModulateBy(GraphTraversal2 traversal, IComparer order)
        {
            throw new NotImplementedException();
        }

        public virtual void ModulateBy(GremlinKeyword.Column column)
        {
            throw new NotImplementedException();
        }

        public virtual void ModulateBy(GremlinKeyword.Column column, GremlinKeyword.Order order)
        {
            throw new NotImplementedException();
        }

        public virtual void ModulateBy(GremlinKeyword.Column column, IComparer order)
        {
            throw new NotImplementedException();
        }
    }
    
    internal class GremlinParentContextOp : GremlinTranslationOperator
    {
        public GremlinVariable InheritedPivotVariable { get; set; }
        public GremlinToSqlContext InheritedContext { get; set; }
        //public List<GremlinMatchPath> InheritedPathList { get; set; }
        public GremlinToSqlContext ParentContext { get; set; }

        internal override GremlinToSqlContext GetContext()
        {
            if (InheritedContext != null) return InheritedContext;
            GremlinToSqlContext newContext = new GremlinToSqlContext();
            //newContext.PathList = InheritedPathList;
            newContext.ParentContext = ParentContext;
            if (InheritedPivotVariable != null)
            {
                GremlinContextVariable newVariable = new GremlinContextVariable(InheritedPivotVariable);
                newVariable.HomeContext = newContext;
                newContext.VariableList.Add(newVariable);
                newContext.PivotVariable = newVariable;
            } 
            return newContext;
        }
    }
}
