﻿namespace Dyfrig.Http

open System
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Text
open Aether
open Aether.Operators
open Dyfrig.Core
open Dyfrig.Core.Operators

(* Content Negotiation
            
   Taken from RFC 7231, Section 5.3
   [http://tools.ietf.org/html/rfc7231#section-5.3] *)

(* Accept

   Taken from RFC 7231, Section 5.3.2. Accept
   [http://tools.ietf.org/html/rfc7231#section-5.3.2] *)

type Accept =
    { MediaType: MediaRange
      MediaTypeParameters: Map<string, string>
      ExtensionParameters: Map<string, string option>
      Weight: float option }

and MediaRange =
    | Closed of ClosedMediaRange
    | Partial of PartialMediaRange
    | Open

and ClosedMediaRange =
    | ClosedMediaRange of MediaType * MediaSubType

and PartialMediaRange =
    | PartialMediaRange of MediaType

and MediaType =
    | MediaType of string

and MediaSubType =
    | MediaSubType of string

(* Accept-Charset

   Taken from RFC 7231, Section 5.3.3. Accept-Charset
   [http://tools.ietf.org/html/rfc7231#section-5.3.3] *)

type AcceptCharset =
    { Charset: Charset
      Weight: float option }

and Charset =
    | Named of NamedCharset
    | Any

and NamedCharset =
    | NamedCharset of string

(* Accept-Encoding

   Taken from RFC 7231, Section 5.3.4. Accept-Encoding
   [http://tools.ietf.org/html/rfc7231#section-5.3.4] *)

type AcceptEncoding =
    { Encoding: Encoding
      Weight: float option }

and Encoding =
    | Named of NamedEncoding
    | Identity
    | Any

and NamedEncoding =
    | NamedEncoding of string

(* Accept-Language

   Taken from RFC 7231, Section 5.3.5. Accept-Language
   [http://tools.ietf.org/html/rfc7231#section-5.3.5] *)

type AcceptLanguage =
    { Language: CultureInfo
      Weight: float option }

(* Precondition
            
   Taken from RFC 7232, Section 3
   [http://tools.ietf.org/html/rfc7232#section-3] *)

type EntityTag =
    | Strong of string
    | Weak of string

(* If-Match

   Taken from RFC 7232, Section 3.1, If-Match
   [http://tools.ietf.org/html/rfc7232#section-3.1] *)

type IfMatch =
    | EntityTags of EntityTag list
    | Any

(* If-None-Match

   Taken from RFC 7232, Section 3.2, If-None-Match
   [http://tools.ietf.org/html/rfc7232#section-3.2] *)

type IfNoneMatch =
    | EntityTags of EntityTag list
    | Any
    
(* Method
    
   Types representing the method of an HTTP request.
   See [http://tools.ietf.org/html/rfc7231] for details. *)

type Method =
    | DELETE 
    | HEAD 
    | GET 
    | OPTIONS 
    | PATCH 
    | POST 
    | PUT 
    | TRACE 
    | Custom of string

(* Protocol
    
   Types representing the protocol of an HTTP request.
   See [http://tools.ietf.org/html/rfc7231] for details. *)

type Protocol =
    | HTTP of float 
    | Custom of string

(* Scheme
    
   Types representing the scheme of an HTTP request.
   See [http://tools.ietf.org/html/rfc7231] for details. *)

type Scheme =
    | HTTP 
    | HTTPS 
    | Custom of string


[<RequireQualifiedAccess>]
module private Option =

    let getOrElse def =
        function | Some x -> x
                 | _ -> def


[<AutoOpen>]
module Negotiation =

    // TODO: Make this much better! It's ugly as hell right now...

    let private (===) s1 s2 =
        String.Equals (s1, s2, StringComparison.OrdinalIgnoreCase)

    (* Content Negotiation
            
       Taken from RFC 7231, Section 5.3
       [http://tools.ietf.org/html/rfc7231#section-5.3] *)

    (* Accept

       Taken from RFC 7231, Section 5.3.2. Accept
       [http://tools.ietf.org/html/rfc7231#section-5.3.2] *)

    let private (|Closed|_|) =
        function | MediaRange.Closed (ClosedMediaRange (MediaType x, MediaSubType y)) -> Some (x, y)
                    | _ -> None

    let private (|Partial|_|) =
        function | MediaRange.Partial (PartialMediaRange (MediaType x)) -> Some x
                    | _ -> None

    let private matchAccept (ClosedMediaRange (MediaType t, MediaSubType s)) =
        function | Closed (t', s') when t === t' && s === s' -> true, 3
                 | Partial t' when t === t' -> true, 2
                 | MediaRange.Open -> true, 1
                 | _ -> false, 0

    let negotiateAccept (available: ClosedMediaRange list) (requested: Accept list) =
        requested
        |> List.sortBy (fun r -> r.Weight |> Option.getOrElse 1.)
        |> List.rev
        |> List.tryPick (fun r ->
            let available =
                available 
                |> List.map (fun a -> a, matchAccept a r.MediaType)
                |> List.filter (fun (_, (m, _)) -> m)
                    
            match available with
            | [] -> None
            | available -> Some (r, available |> List.maxBy (fun (_, (_, s)) -> s)))
        |> Option.map (fun (_, (selected, _)) -> selected)

    (* Accept-Charset

       Taken from RFC 7231, Section 5.3.3. Accept-Charset
       [http://tools.ietf.org/html/rfc7231#section-5.3.3] *)

    let negotiateCharset (available: NamedCharset list) (requested: AcceptCharset list) =
        None

    (* Accept-Encoding

       Taken from RFC 7231, Section 5.3.4. Accept-Encoding
       [http://tools.ietf.org/html/rfc7231#section-5.3.4] *)

    let negotiateEncoding (available: NamedEncoding list) (requested: AcceptEncoding list) =
        None

    (* Accept-Language

       Taken from RFC 7231, Section 5.3.5. Accept-Language
       [http://tools.ietf.org/html/rfc7231#section-5.3.5] *)

    let negotiateLanguage (available: CultureInfo list) (requested: AcceptLanguage list) =
        None


[<AutoOpen>]
module internal Parsing =

    open FParsec


    let parse p s =
        match run p s with
        | Success (x, _, _) -> Some x
        | Failure (_, _, _) -> None


    [<AutoOpen>]
    module RFC_5234 =

        [<AutoOpen>]
        module Appendix_B_1 =

            (* Core Rules

               Taken from RFC 5234, Appendix B.1. Core Rules
               [http://tools.ietf.org/html/rfc5234#appendix-B.1] *)

//            let CR = 
//                char 0x0d

            let DQUOTE = 
                char 0x22

            let HTAB = 
                char 0x09

            let SP = 
                char 0x20

            let ALPHA = 
                Set.unionMany 
                    [ set (List.map char [0x41 .. 0x5a])
                      set (List.map char [0x61 .. 0x7a]) ]

            let DIGIT = 
                set (List.map char [0x30 .. 0x39])

//            let VCHAR = 
//                set (List.map char [0x21 .. 0x7e])

            let WSP = 
                set [ SP; HTAB ]

            let isALPHA c = 
                Set.contains c ALPHA

            let isDIGIT c = 
                Set.contains c DIGIT

            let isWSP c = 
                Set.contains c WSP


    [<AutoOpen>]
    module RFC_7230 =

        [<AutoOpen>]
        module Section_3_2_3 =

            (* Whitespace

               Taken from RFC 7230, Section 3.2.3. Whitespace
               [http://tools.ietf.org/html/rfc7230#section-3.2.3] *)
        
            let OWS = 
                skipManySatisfy isWSP

    //        let RWS = 
    //            skipMany1Satisfy isWSP


        [<AutoOpen>]
        module Section_3_2_6 =

            (* Field Value Components

               Taken from RFC 7230, Section 3.2.6. Field Value Components
               [http://tools.ietf.org/html/rfc7230#section-3.2.6] *)

            let private nonDelimiters =
                set [ '!'; '#'; '$'; '%'; '&'; '\''; '*'
                      '+'; '-'; '.'; '^'; '_'; '`'; '|'; '~' ]

            let TCHAR = 
                Set.unionMany 
                    [ nonDelimiters
                      ALPHA
                      DIGIT ]

            let isTCHAR c =
                Set.contains c TCHAR

            let token = 
                many1Satisfy isTCHAR


        [<AutoOpen>]
        module Section_7 =

            (* ABNF List Extension: #rule

               Taken from RFC 7230, Section 7. ABNF List Extension: #rule
               [http://tools.ietf.org/html/rfc7230#section-7] *)

            let private infixHead s p =
                (attempt p |>> Some) <|> (s >>% None)

            let private infixTail s p =
                many (OWS >>? s >>? OWS >>? opt p)

            (* Note:
               The infix and prefix parsers are designed to convey as accurately as possible the 
               meaning of the ABNF #rule extension including the laxity of specification for backward 
               compatibility. Whether they are a perfectly true representation is open to debate, 
               but they should perform sensibly under normal conditions. *)

            let infix s p = 
                infixHead s p .>>. infixTail s p .>> OWS |>> fun (x, xs) ->  x :: xs |> List.choose id

            let infix1 s p =
                notEmpty (infix s p)

            let prefix s p =
                many (OWS >>? s >>? OWS >>? p)

//            let prefix1 s p =
//                notEmpty (prefix s p)


    [<AutoOpen>]
    module RFC_7231 =

        [<AutoOpen>]
        module Section_5_3_1 =

            (* Quality Values

               Taken from RFC 7231, Section 5.3.1. Quality Values
               [http://tools.ietf.org/html/rfc7231#section-5.3.1] *)

            let private valueOrDefault =
                function
                | Some x -> float (sprintf "0.%s" x)
                | _ -> 0.

            let private d3 =
                 manyMinMaxSatisfy 0 3 isDIGIT .>> notFollowedBy (skipSatisfy isDIGIT)

            let private isZero =
                isAnyOf [ '0' ]

            let private d03 =
                skipManyMinMaxSatisfy 0 3 isZero .>> notFollowedBy (skipSatisfy isDIGIT)

            let private qvalue =
                choice
                    [ skipChar '0' >>. opt (skipChar '.' >>. d3) |>> valueOrDefault
                      skipChar '1' >>. optional (skipChar '.' >>. d03) >>% 1. ]

            let weight =
                skipChar ';' >>. OWS >>. skipStringCI "q=" >>. qvalue .>> OWS


        [<AutoOpen>]
        module Section_5_3_2 =
        
            (* Accept

               Taken from RFC 7231, Section 5.3.2. Accept
               [http://tools.ietf.org/html/rfc7231#section-5.3.2] *)

            // TODO: tokens are not the only values here, dquoted strings need implementing

            let private acceptExtension =
                token .>>. opt (skipChar '=' >>. token)

            let private acceptExtensions =
                prefix (skipChar ';') acceptExtension 
                |>> Map.ofList

            let private acceptParameters =
                weight .>> OWS .>>. acceptExtensions

            let private parameter =
                notFollowedBy (OWS >>. skipStringCI "q=") >>. token .>> skipChar '=' .>>. token

            let private parameters =
                prefix (skipChar ';') parameter 
                |>> Map.ofList

            let private openRange = 
                skipString "*/*"
                |>> fun _ -> MediaRange.Open

            let private partialRange = 
                token .>> skipString "/*"
                |>> fun x -> MediaRange.Partial (PartialMediaRange (MediaType x))

            let private fullRange = 
                token .>> skipChar '/' .>>. token
                |>> fun (x, y) -> MediaRange.Closed (ClosedMediaRange (MediaType x, MediaSubType y))

            let private range = 
                choice [
                    attempt openRange
                    attempt partialRange
                    fullRange ]

            let private mediaRange = 
                range .>> OWS .>>. parameters

            let accept = 
                infix (skipChar ',') (mediaRange .>> OWS .>>. opt acceptParameters)
                |>> List.map (fun ((mediaRange, parameters), acceptParameters) ->
                    { MediaType = mediaRange
                      MediaTypeParameters = parameters
                      Weight = acceptParameters |> Option.map fst
                      ExtensionParameters = acceptParameters |> Option.map snd |> Option.getOrElse Map.empty })


        [<AutoOpen>]
        module Section_5_3_3 =

            (* Accept-Charset

               Taken from RFC 7231, Section 5.3.3. Accept-Charset
               [http://tools.ietf.org/html/rfc7231#section-5.3.3] *)

            let private openCharset =
                skipChar '*'
                |>> fun _ -> Charset.Any

            let private namedCharset =
                token
                |>> fun s -> Charset.Named (NamedCharset s)

            let private charset = 
                choice [
                    openCharset
                    namedCharset ]

            let acceptCharset =
                infix1 (skipChar ',') (charset .>> OWS .>>. opt weight)
                |>> List.map (fun (charset, weight) ->
                    { Charset = charset
                      Weight = weight })


        [<AutoOpen>]
        module Section_5_3_4 =

            (* Accept-Encoding

               Taken from RFC 7231, Section 5.3.4. Accept-Encoding
               [http://tools.ietf.org/html/rfc7231#section-5.3.4] *)

            let private openEncoding =
                skipChar '*'
                |>> fun _ -> Encoding.Any

            let private identityEncoding =
                skipStringCI "identity"
                |>> fun _ -> Encoding.Identity

            let private namedEncoding =
                token
                |>> fun s -> Encoding.Named (NamedEncoding s)

            let private encoding =
                choice [
                    openEncoding
                    identityEncoding
                    namedEncoding ]

            let acceptEncoding =
                infix (skipChar ',') (encoding .>> OWS .>>. opt weight)
                |>> List.map (fun (encoding, weight) ->
                    { Encoding = encoding
                      Weight = weight })

        [<AutoOpen>]
        module Section_5_3_5 =

            (* Accept-Language

               Taken from RFC 7231, Section 5.3.5. Accept-Language
               [http://tools.ietf.org/html/rfc7231#section-5.3.5] *)

            (* Note: Language range taken as the Basic Language Range
               definition from RFC 4647, Section 3.1.3.1 *)

            let private languageRangeComponent =
                manyMinMaxSatisfy 1 8 isALPHA

            let private languageRange =
                languageRangeComponent .>>. opt (skipChar '-' >>. languageRangeComponent)
                |>> function 
                    | range, Some sub -> CultureInfo (sprintf "%s-%s" range sub)
                    | range, _ -> CultureInfo (range)

            let acceptLanguage =
                infix (skipChar ',') (languageRange .>> OWS .>>. opt weight)
                |>> List.map (fun (languageRange, weight) ->
                    { Language = languageRange
                      Weight = weight })


    [<AutoOpen>]
    module RFC_7232 =

        (* TODO: This is a naive formulation of an entity tag and does not
           properly support the grammar, particularly weak references, which
           should be implemented ASAP *)

        let entityTag =
            skipChar DQUOTE >>. token .>> skipChar DQUOTE


        [<AutoOpen>]
        module Section_3_1 =

            (* If-Match

               Taken from RFC 7232, Section 3.1, If-Match
               [http://tools.ietf.org/html/rfc7232#section-3.1] *)

            let ifMatch =
                choice [
                    skipChar '*' |>> fun _ -> IfMatch.Any
                    infix (skipChar ',') entityTag |>> fun x -> 
                        IfMatch.EntityTags (List.map Strong x) ]


        [<AutoOpen>]
        module Section_3_2 =

            (* If-None-Match

               Taken from RFC 7232, Section 3.2, If-None-Match
               [http://tools.ietf.org/html/rfc7232#section-3.2] *)

            let ifNoneMatch =
                choice [
                    skipChar '*' |>> fun _ -> IfNoneMatch.Any
                    infix (skipChar ',') entityTag |>> fun x -> 
                        IfNoneMatch.EntityTags (List.map Strong x) ]


[<AutoOpen>]
module internal Serialization =

    let private weightToString =
        Option.map (sprintf ";q=%.3g") >> Option.getOrElse ""

    
    [<RequireQualifiedAccess>]
    module Accept =

        let private fromString =
            parse accept

        let private toString _ =
            ""

        let iso =
            fromString, toString


    [<RequireQualifiedAccess>]
    module AcceptCharset =

        let private fromString =
            parse acceptCharset

        let private toString =
            List.map (fun x ->
                let charset =
                    match x.Charset with
                    | Charset.Named (NamedCharset x) -> x
                    | Charset.Any -> "*"                    

                sprintf "%s%s" charset (weightToString x.Weight))
            >> String.concat ","

        let iso =
            fromString, toString


    [<RequireQualifiedAccess>]
    module AcceptEncoding =

        let private fromString =
            parse acceptEncoding

        let private toString =
            List.map (fun x ->
                let encoding =
                    match x.Encoding with
                    | Encoding.Named (NamedEncoding x) -> x
                    | Encoding.Identity -> "identity"
                    | Encoding.Any -> "*"                    

                sprintf "%s%s" encoding (weightToString x.Weight)) 
            >> String.concat ","

        let iso =
            fromString, toString


    [<RequireQualifiedAccess>]
    module AcceptLanguage =

        let private fromString =
            parse acceptLanguage

        let private toString =
            List.map (fun x -> sprintf "%s%s" x.Language.Name (weightToString x.Weight)) 
            >> String.concat ","

        let iso =
            fromString, toString


    [<RequireQualifiedAccess>]
    module DateTime =

        let private fromString x =
            let format = CultureInfo.InvariantCulture.DateTimeFormat
            let adjustment = DateTimeStyles.AdjustToUniversal

            match DateTime.TryParse (x, format, adjustment) with
            | true, x -> Some x
            | _ -> None

        let private toString (x: DateTime) =
            x.ToString("r")

        let iso =
            fromString, toString


    [<RequireQualifiedAccess>]
    module IfMatch =

        let private fromString =
            parse ifMatch

        let private toString =
            function 
            | IfMatch.EntityTags entityTags -> 
                entityTags
                |> List.map (fun (Strong x | Weak x) -> sprintf "\"%s\"" x) 
                |> String.concat ","
            | IfMatch.Any -> "*"

        let iso =
            fromString, toString


    [<RequireQualifiedAccess>]
    module IfNoneMatch =

        let private fromString =
            parse ifNoneMatch

        let private toString =
            function 
            | IfNoneMatch.EntityTags entityTags -> 
                entityTags
                |> List.map (fun (Strong x | Weak x) -> sprintf "\"%s\"" x) 
                |> String.concat ","
            | IfNoneMatch.Any -> "*"

        let iso =
            fromString, toString


    [<RequireQualifiedAccess>]
    module MaxForwards =

        let private fromString x =
            match Int32.TryParse x with
            | true, x -> Some x
            | _ -> None

        let iso =
            fromString, string


    [<RequireQualifiedAccess>]
    module Method =

        let private fromString =
            function | "DELETE" -> DELETE 
                     | "HEAD" -> HEAD 
                     | "GET" -> GET 
                     | "OPTIONS" -> OPTIONS
                     | "PATCH" -> PATCH 
                     | "POST" -> POST 
                     | "PUT" -> PUT 
                     | "TRACE" -> TRACE
                     | x -> Method.Custom x        
                     
        let private toString =
            function | DELETE -> "DELETE" 
                     | HEAD -> "HEAD" 
                     | GET -> "GET" 
                     | OPTIONS -> "OPTIONS"
                     | PATCH -> "PATCH" 
                     | POST -> "POST" 
                     | PUT -> "PUT"  
                     | TRACE -> "TRACE"
                     | Method.Custom x -> x

        let iso =
            fromString, toString


    [<RequireQualifiedAccess>]
    module Protocol =

        let private fromString =
            function | "HTTP/1.0" -> Protocol.HTTP 1.0 
                     | "HTTP/1.1" -> Protocol.HTTP 1.1 
                     | x -> Protocol.Custom x

        let private toString =
            function | Protocol.HTTP x -> sprintf "HTTP/%f" x 
                     | Protocol.Custom x -> x

        let iso =
            fromString, toString


    [<RequireQualifiedAccess>]
    module Scheme =

        let private fromString =
            function | "http" -> HTTP 
                     | "https" -> HTTPS 
                     | x -> Scheme.Custom x

        let private toString =    
            function | HTTP -> "http"
                     | HTTPS -> "https" 
                     | Scheme.Custom x -> x

        let iso =
            fromString, toString


    [<RequireQualifiedAccess>]
    module Query =

        let private fromString =
            function | "" -> Map.empty
                     | s ->
                         s.Split [| '&' |]
                         |> Array.map (fun x -> x.Split [| '=' |])
                         |> Array.map (fun x -> x.[0], x.[1])
                         |> Map.ofArray

        let private toString =
            fun m ->
                Map.toArray m
                |> Array.map (fun (x, y) -> sprintf "%s=%s" x y)
                |> String.concat "&"

        let iso =
            fromString, toString


[<AutoOpen>]
module Lenses =

    // Boxing

    let boxIso<'T> : Iso<obj,'T> =
        unbox<'T>, box

    // Dictionary

    let dictLens k : Lens<IDictionary<'k,'v>, 'v> =
        (fun d -> d.[k]),
        (fun v d -> d.[k] <- v; d)

    let dictPLens k : PLens<IDictionary<'k,'v>, 'v> =
        (fun d -> d.TryGetValue k |> function | true, v -> Some v | _ -> None),
        (fun v d -> d.[k] <- v; d)

    // Headers

    let headerIso : Iso<string [], string> =
        (fun s -> String.concat "," s),
        (fun s -> [| s |])


[<RequireQualifiedAccess>]
module Request =

    let body =
             dictLens Constants.requestBody
        <--> boxIso<Stream>

    let header key =
             dictLens Constants.requestHeaders
        <--> boxIso<IDictionary<string, string []>>
        >-?> dictPLens key

    let meth = 
             dictLens Constants.requestMethod
        <--> boxIso<string>
        <--> Method.iso

    let path = 
             dictLens Constants.requestPath
        <--> boxIso<string>

    let pathBase =
             dictLens Constants.requestPathBase
        <--> boxIso<string>

    let protocol : Lens<OwinEnv, Protocol> =
             dictLens Constants.requestProtocol
        <--> boxIso<string>
        <--> Protocol.iso

    let scheme : Lens<OwinEnv, Scheme> = 
             dictLens Constants.requestScheme
        <--> boxIso<string>
        <--> Scheme.iso

    let query key =
             dictLens Constants.requestQueryString
        <--> boxIso<string>
        <--> Query.iso
        >-?> mapPLens key


    [<RequireQualifiedAccess>]
    module Conditionals =

        let ifMatch =
                 header "If-Match"
            <?-> headerIso
            <??> IfMatch.iso

        let ifNoneMatch =
                 header "If-None-Match"
            <?-> headerIso
            <??> IfNoneMatch.iso

        let ifModifiedSince =
                 header "If-Modified-Since"
            <?-> headerIso
            <??> DateTime.iso

        let ifUnmodifiedSince =
                 header "If-Unmodified-Since"
            <?-> headerIso
            <??> DateTime.iso


    [<RequireQualifiedAccess>]
    module ContentNegotiation =

        let accept =
                 header "Accept"
            <?-> headerIso
            <??> Accept.iso

        let acceptCharset =
                 header "Accept-Charset"
            <?-> headerIso
            <??> AcceptCharset.iso

        let acceptEncoding =
                 header "Accept-Encoding"
            <?-> headerIso
            <??> AcceptEncoding.iso

        let acceptLanguage =
                 header "Accept-Language"
            <?-> headerIso
            <??> AcceptLanguage.iso


    [<RequireQualifiedAccess>]
    module Controls =

        // TODO: Investigate a strongly typed
        // abstraction over a Host value.

        let host =
            header "Host"
            <?-> headerIso

        let maxForwards =
            header "Max-Forwards"
            <?-> headerIso
            <??> MaxForwards.iso


[<RequireQualifiedAccess>]
module Response =

    let body =
             dictLens Constants.responseBody
        <--> boxIso<Stream>

    let header key =
             dictLens Constants.responseHeaders
        <--> boxIso<IDictionary<string, string []>>
        >-?> dictPLens key

    let reasonPhrase =
             dictPLens Constants.responseReasonPhrase
        <?-> boxIso<string>

    let statusCode =
             dictPLens Constants.responseStatusCode
        <?-> boxIso<int>


[<AutoOpen>]
module Monad =

    /// Gets part of the OwinEnv using an Aether lens within an OWIN monad
    let getLM l = 
        getL l <!> getM

    /// Sets part of the OwinEnv using an Aether lens within an OWIN monad
    let setLM l v = 
        setL l v |> modM

    /// Modifies part of the OwinEnv using an Aether lens within an OWIN monad
    let modLM l f = 
        modL l f |> modM

    /// Gets part of the OwinEnv using a partial Aether lens within an OWIN monad
    let getPLM l = 
        getPL l <!> getM

    /// Sets part of the OwinEnv using a partial Aether lens within an OWIN monad
    let setPLM l v = 
        setPL l v |> modM

    /// Modifies part of the OwinEnv using a partial Aether lens within an OWIN monad
    let modPLM l f = 
        modL l f |> modM
