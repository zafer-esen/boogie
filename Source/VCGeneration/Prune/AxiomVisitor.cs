using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;

namespace Microsoft.Boogie
{
  internal class AxiomVisitor : DependencyEvaluator
  {
    private HashSet<String> usedBySymbols;

    public AxiomVisitor(Axiom a) : base(a)
    {
      var usedByAttribute = QKeyValue.FindStringAttribute(declaration.Attributes, "used_by");
      // TODO: note - used by symbols should be updated during monomorphization or this would not work!
      usedBySymbols = usedByAttribute?.Split(',').Select(s => s.Trim()).ToHashSet();
    }

    public static DependencyEvaluator GetDependencies(Axiom axiom)
    {
      var result = new AxiomVisitor(axiom);
      result.Visit(axiom);
      return result;
    }
    
    private void VisitTriggerCustom(Trigger t) {
      var visitor = new TriggerVisitor();
      foreach (var trigger in t.Tr) {
        visitor.VisitExpr(trigger);
      }
      var incomingSet = visitor.Declarations.ToArray();
      AddIncoming(incomingSet);
    }

    void TryAddIncoming(NamedDeclaration newIncoming)
    {
      if (QKeyValue.FindBoolAttribute(declaration.Attributes, "include_dep") || 
          usedBySymbols != null && usedBySymbols.Contains(newIncoming.Name))
      {
        AddIncoming(newIncoming);
      }
    }

    public override Expr VisitExpr(Expr node) {
      if (node is IdentifierExpr iExpr && iExpr.Decl is Constant c) {
        TryAddIncoming(c);
        AddOutgoing(c);
      } else if (node is NAryExpr e && e.Fun is FunctionCall f) {
        TryAddIncoming(f.Func);
        AddOutgoing(f.Func);
      } else if (node is NAryExpr n) {
        var applicable = n.Fun;
        if (applicable is UnaryOperator op) {
          Contract.Assert(op.Op == UnaryOperator.Opcode.Neg || op.Op == UnaryOperator.Opcode.Not);
          Contract.Assert(n.Args.Count() == 1);
          n.Args[0].pos = Expr.NegatePosition(n.Args[0].pos);
        } else if (applicable is BinaryOperator bin) {
          Contract.Assert(n.Args.Count() == 2);
          if (bin.Op == BinaryOperator.Opcode.And
              || bin.Op == BinaryOperator.Opcode.Or) {
          } else if (bin.Op == BinaryOperator.Opcode.Imp) {
            n.Args[0].pos = Expr.NegatePosition(n.Args[0].pos);
          } else {
            n.Args.ToList().ForEach(a => a.pos = Expr.Position.Neither);
          }
        } else {
          n.Args.ToList().ForEach(a => a.pos = Expr.Position.Neither);
        }
      } else if (node is BinderExpr be && be is QuantifierExpr qe) {
        Trigger start = qe.Triggers;
        while(start != null) {
          VisitTriggerCustom(start);
          start = start.Next;
        }
        var discardBodyIncoming = (qe is ForallExpr { pos: Expr.Position.Pos } && qe.Triggers != null)
                                  || (qe is ExistsExpr { pos: Expr.Position.Neg } && qe.Triggers != null);
        be.Body.pos = Expr.Position.Neither;
        if (discardBodyIncoming) {
          var incomingOld = incomingSets;
          incomingSets = new();
          VisitExpr(be.Body); // this will still edit the outgoing edges and types
          incomingSets = incomingOld;
        } else {
          VisitExpr(be.Body);
        }
        return null;
      } else if (node is OldExpr o) {
        o.Expr.pos = Expr.Position.Neither;
      } else if (node is CodeExpr) {
        // no blocks in axioms
        Contract.Assert(false);
      } else if (node is BvExtractExpr bve) {
        bve.Bitvector.pos = Expr.Position.Neither;
      } else if (node is BvConcatExpr bvc) {
        bvc.E0.pos = Expr.Position.Neither;
        bvc.E1.pos = Expr.Position.Neither;
      } else if (node is BinderExpr bexp) {
        bexp.Body.pos = Expr.Position.Neither;
      } else if (node is LetExpr l) {
        l.Body.pos = Expr.Position.Neither;
      } else {
        if (node is LiteralExpr || node is IdentifierExpr) {

        } else {
          Console.WriteLine(node);
          Contract.Assert(false);
        }
      }
      return base.VisitExpr(node);
    }

    public override Type VisitType(Type node)
    {
      types.Add(node);
      return base.VisitType(node);
    }
  }
}