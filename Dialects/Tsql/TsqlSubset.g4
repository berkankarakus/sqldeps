grammar TsqlSubset;

script
    : statement* EOF
    ;

statement
    : selectStatement SEMICOLON?
    | createTableStatement SEMICOLON?
    ;

selectStatement
    : SELECT selectList FROM qualifiedName alias? tableHint? whereClause?
    ;

createTableStatement
    : CREATE TABLE qualifiedName LEFT_PAREN columnDefinition (COMMA columnDefinition)* RIGHT_PAREN
    ;

selectList
    : STAR
    | selectItem (COMMA selectItem)*
    ;

selectItem
    : qualifiedName alias?
    ;

columnDefinition
    : identifier typeName
    ;

typeName
    : identifier
    ;

alias
    : AS? identifier
    ;

tableHint
    : WITH LEFT_PAREN identifier RIGHT_PAREN
    ;

whereClause
    : WHERE expression
    ;

expression
    : value comparisonOperator value
    ;

comparisonOperator
    : EQUAL
    | NOT_EQUAL
    | LESS_OR_EQUAL
    | GREATER_OR_EQUAL
    | LESS
    | GREATER
    ;

value
    : qualifiedName
    | STRING
    | NUMBER
    ;

qualifiedName
    : identifier (DOT identifier)*
    ;

identifier
    : IDENTIFIER
    | BRACKET_IDENTIFIER
    | QUOTED_IDENTIFIER
    ;

SELECT: S E L E C T;
FROM: F R O M;
WHERE: W H E R E;
CREATE: C R E A T E;
TABLE: T A B L E;
WITH: W I T H;
AS: A S;
STAR: '*';
SEMICOLON: ';';
COMMA: ',';
DOT: '.';
LEFT_PAREN: '(';
RIGHT_PAREN: ')';
EQUAL: '=';
NOT_EQUAL: '<>' | '!=';
LESS_OR_EQUAL: '<=';
GREATER_OR_EQUAL: '>=';
LESS: '<';
GREATER: '>';
STRING: '\'' ('\'\'' | ~'\'')* '\'';
NUMBER: [0-9]+ ('.' [0-9]+)?;
BRACKET_IDENTIFIER: '[' (']]' | ~']')* ']';
QUOTED_IDENTIFIER: '"' ('""' | ~'"')+ '"';
IDENTIFIER: [a-zA-Z_] [a-zA-Z_0-9$]*;
LINE_COMMENT: '--' ~[\r\n]* -> channel(HIDDEN);
BLOCK_COMMENT: '/*' .*? '*/' -> channel(HIDDEN);
WHITESPACE: [ \t\r\n]+ -> channel(HIDDEN);

fragment A: [aA];
fragment B: [bB];
fragment C: [cC];
fragment D: [dD];
fragment E: [eE];
fragment F: [fF];
fragment G: [gG];
fragment H: [hH];
fragment I: [iI];
fragment L: [lL];
fragment M: [mM];
fragment O: [oO];
fragment R: [rR];
fragment S: [sS];
fragment T: [tT];
fragment W: [wW];
