using System.Text.Json;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Build;

/// <summary>
/// The contract with the island server and the API for objects: how a command is written, how an object's state is turned into the payload the server expects,
/// and what a refusal code means in plain Portuguese. Nothing here touches the scene; the synchronizer and the controllers use it.
/// </summary>
public static class BuildPersistenceManager
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>One build command, ready for <c>NetworkManager.SubmitBuildOperation</c>.</summary>
    public static string Command(string operation, Guid? objectId, int expectedRevision, object? payload) =>
        JsonSerializer.Serialize(new { operation, objectId, @object = payload, expectedRevision }, Web);

    /// <summary>The object as the server takes it in a create or update command.</summary>
    public static Dictionary<string, object?> Payload(BuildObjectState s) => new()
    {
        ["primitiveType"] = s.PrimitiveType, ["positionX"] = s.PositionX, ["positionY"] = s.PositionY, ["positionZ"] = s.PositionZ,
        ["rotationX"] = s.RotationX, ["rotationY"] = s.RotationY, ["rotationZ"] = s.RotationZ, ["scaleX"] = s.ScaleX, ["scaleY"] = s.ScaleY, ["scaleZ"] = s.ScaleZ,
        ["materialJson"] = s.MaterialJson, ["geometryJson"] = s.GeometryJson, ["physicsJson"] = string.IsNullOrWhiteSpace(s.PhysicsJson) ? "{\"collision\":true}" : s.PhysicsJson,
        ["groupId"] = s.GroupId, ["parentObjectId"] = s.ParentObjectId, ["expectedRevision"] = s.Revision, ["name"] = s.Name, ["description"] = s.Description,
        ["contentJson"] = string.IsNullOrWhiteSpace(s.ContentJson) ? "[]" : s.ContentJson, ["pivotX"] = s.PivotX, ["pivotY"] = s.PivotY, ["pivotZ"] = s.PivotZ
    };

    public static BuildObjectState Clone(BuildObjectState s) => JsonSerializer.Deserialize<BuildObjectState>(JsonSerializer.Serialize(s, Web), Web)!;

    public static BuildObjectState? ParseState(string json)
    {
        try { return JsonSerializer.Deserialize<BuildObjectState>(json, Web); }
        catch (JsonException) { return null; }
    }

    /// <summary>What a refusal from the server means for the person (the server decides; the editor only explains).</summary>
    public static string RejectionText(string code)
    {
        if (code.StartsWith("locked:", StringComparison.Ordinal)) return $"{code[7..]} está editando este objeto agora. Tente de novo em instantes.";
        return code switch
        {
            "forbidden" => "Você não tem permissão para construir aqui.",
            "parcel_forbidden" => "Este terreno não permite que você construa.",
            "parcel_banned" => "Você está banido deste terreno e não pode construir nele.",
            "scripts_not_allowed" => "Este terreno não permite scripts em objetos de quem não é o dono.",
            "media_not_allowed" => "Este terreno não permite mídia (imagem ou vídeo) em objetos de quem não é o dono.",
            "not_owner" => "Este objeto não é seu: só o dono, o dono do terreno ou um administrador podem alterá-lo.",
            "not_allowed_to_return" => "Você não pode devolver esses objetos (só o dono do terreno ou um administrador).",
            "cost_limit" => "O custo total da ilha chegou ao limite; use formas mais simples ou apague algo.",
            "object_quota" or "server_rejected_409" => "A ilha atingiu o limite de objetos.",
            "parcel_object_limit" => "O terreno atingiu o limite de objetos.",
            "parcel_cost_limit" => "O terreno atingiu o limite de custo.",
            "invalid_object" => "O servidor recusou algum valor do objeto (tamanho, forma, textura ou conteúdo).",
            "object_protected" => "Esse objeto está protegido contra edição acidental. Desproteja-o para mexer nele.",
            "link_cycle" => "Esse vínculo faria uma peça ficar pendurada em si mesma.",
            "broken_link" => "O vínculo ficaria com peças soltas: inclua as peças penduradas.",
            "invalid_link" => "Vínculo inválido: uma peça só pode ficar pendurada em outra do mesmo conjunto.",
            "link_too_deep" => "A hierarquia ficou funda demais (no máximo 16 níveis).",
            "linkset_too_large" => "Um conjunto vinculado tem no máximo 256 peças.",
            "no_modify" => "Este objeto não pode ser modificado (as peças de que é feito não permitem). Você ainda pode movê-lo, girá-lo ou devolvê-lo.",
            "no_copy_in_use" => "Essa peça não pode ser copiada e já está em outro objeto seu. Devolva o outro objeto primeiro.",
            "item_busy" => "Esse item está em uso agora (oferta, venda ou colocação em andamento).",
            "not_found" => "Esse objeto não existe mais.",
            "invalid_click_action" => "Ação de clique desconhecida.",
            "buy_needs_sale" => "Para usar “Comprar objeto” o objeto precisa estar à venda.",
            "invalid_sale_type" => "Modalidade de venda inválida.",
            "invalid_price" => "Preço inválido: use um número inteiro de 0 a 10.000.000.",
            "cannot_raise_rights" => "Você não pode dar direitos que não tem.",
            "no_copy" => "Sem o direito de copiar, não dá para vender uma cópia.",
            "no_transfer" => "Sem o direito de transferir, não dá para vender o original.",
            "not_for_sale" => "Este objeto não está mais à venda.",
            "price_changed" => "O preço mudou: abra a compra de novo para ver o valor atual.",
            "insufficient_balance" => "Saldo insuficiente para esta compra.",
            "own_object" => "Este objeto já é seu.",
            "wallet_blocked" => "A carteira de quem compra ou de quem vende está bloqueada.",
            "nothing_to_sell" => "Este objeto não tem conteúdo para vender.",
            "stale" => "Outra pessoa comprou primeiro ou a compra já estava em andamento; nada foi cobrado.",
            "not_group_member" => "Você não faz parte desse grupo.",
            "lock_limit" => "Você está segurando objetos demais ao mesmo tempo.",
            "revision_conflict" => "Outra pessoa alterou este objeto antes: sua edição foi recusada e a tela mostra a versão dela. Refaça a edição se ainda quiser.",
            "rate_limit" => "Muitas alterações seguidas; aguarde um instante.",
            "service_unavailable" => "O servidor de construção não respondeu; tente novamente.",
            _ => $"Construção rejeitada: {code}"
        };
    }
}
